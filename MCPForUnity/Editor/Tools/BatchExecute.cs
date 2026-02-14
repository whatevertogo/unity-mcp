using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Executes multiple MCP commands within a single Unity-side handler. Commands are executed sequentially
    /// on the main thread to preserve determinism and Unity API safety.
    /// </summary>
    [McpForUnityTool("batch_execute", AutoRegister = false)]
    public static class BatchExecute
    {
        /// <summary>Default limit when no EditorPrefs override is set.</summary>
        internal const int DefaultMaxCommandsPerBatch = 25;

        /// <summary>Hard ceiling to prevent extreme editor freezes regardless of user setting.</summary>
        internal const int AbsoluteMaxCommandsPerBatch = 100;

        /// <summary>
        /// Returns the user-configured max commands per batch, clamped between 1 and <see cref="AbsoluteMaxCommandsPerBatch"/>.
        /// </summary>
        internal static int GetMaxCommandsPerBatch()
        {
            int configured = EditorPrefs.GetInt(EditorPrefKeys.BatchExecuteMaxCommands, DefaultMaxCommandsPerBatch);
            return Math.Clamp(configured, 1, AbsoluteMaxCommandsPerBatch);
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("'commands' payload is required.");
            }

            var commandsToken = @params["commands"] as JArray;
            if (commandsToken == null || commandsToken.Count == 0)
            {
                return new ErrorResponse("Provide at least one command entry in 'commands'.");
            }

            int maxCommands = GetMaxCommandsPerBatch();
            if (commandsToken.Count > maxCommands)
            {
                return new ErrorResponse(
                    $"A maximum of {maxCommands} commands are allowed per batch (configurable in MCP Tools window, hard max {AbsoluteMaxCommandsPerBatch}).");
            }

            bool failFast = @params.Value<bool?>("failFast") ?? false;
            bool parallelRequested = @params.Value<bool?>("parallel") ?? false;
            int? maxParallel = @params.Value<int?>("maxParallelism");

            if (parallelRequested)
            {
                McpLog.Warn("batch_execute parallel mode requested, but commands will run sequentially on the main thread for safety.");
            }

            var commandResults = new List<object>(commandsToken.Count);
            int invocationSuccessCount = 0;
            int invocationFailureCount = 0;
            bool anyCommandFailed = false;

            foreach (var token in commandsToken)
            {
                if (token is not JObject commandObj)
                {
                    invocationFailureCount++;
                    anyCommandFailed = true;
                    commandResults.Add(new
                    {
                        tool = (string)null,
                        callSucceeded = false,
                        error = "Command entries must be JSON objects."
                    });
                    if (failFast)
                    {
                        break;
                    }
                    continue;
                }

                string toolName = commandObj["tool"]?.ToString();
                var rawParams = commandObj["params"] as JObject ?? new JObject();
                var commandParams = NormalizeParameterKeys(rawParams);

                if (string.IsNullOrWhiteSpace(toolName))
                {
                    invocationFailureCount++;
                    anyCommandFailed = true;
                    commandResults.Add(new
                    {
                        tool = toolName,
                        callSucceeded = false,
                        error = "Each command must include a non-empty 'tool' field."
                    });
                    if (failFast)
                    {
                        break;
                    }
                    continue;
                }

                // Block disabled tools (mirrors TransportCommandDispatcher check)
                var toolMeta = MCPServiceLocator.ToolDiscovery.GetToolMetadata(toolName);
                if (toolMeta != null && !MCPServiceLocator.ToolDiscovery.IsToolEnabled(toolName))
                {
                    invocationFailureCount++;
                    anyCommandFailed = true;
                    commandResults.Add(new
                    {
                        tool = toolName,
                        callSucceeded = false,
                        result = new ErrorResponse($"Tool '{toolName}' is disabled in the Unity Editor.")
                    });
                    if (failFast) break;
                    continue;
                }

                try
                {
                    var result = await CommandRegistry.InvokeCommandAsync(toolName, commandParams).ConfigureAwait(true);
                    bool callSucceeded = DetermineCallSucceeded(result);
                    if (callSucceeded)
                    {
                        invocationSuccessCount++;
                    }
                    else
                    {
                        invocationFailureCount++;
                        anyCommandFailed = true;
                    }

                    commandResults.Add(new
                    {
                        tool = toolName,
                        callSucceeded,
                        result
                    });

                    if (!callSucceeded && failFast)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    invocationFailureCount++;
                    anyCommandFailed = true;
                    commandResults.Add(new
                    {
                        tool = toolName,
                        callSucceeded = false,
                        error = ex.Message
                    });

                    if (failFast)
                    {
                        break;
                    }
                }
            }

            bool overallSuccess = !anyCommandFailed;
            var data = new
            {
                results = commandResults,
                callSuccessCount = invocationSuccessCount,
                callFailureCount = invocationFailureCount,
                parallelRequested,
                parallelApplied = false,
                maxParallelism = maxParallel
            };

            return overallSuccess
                ? new SuccessResponse("Batch execution completed.", data)
                : new ErrorResponse("One or more commands failed.", data);
        }

        private static bool DetermineCallSucceeded(object result)
        {
            if (result == null)
            {
                return true;
            }

            if (result is IMcpResponse response)
            {
                return response.Success;
            }

            if (result is JObject obj)
            {
                var successToken = obj["success"];
                if (successToken != null && successToken.Type == JTokenType.Boolean)
                {
                    return successToken.Value<bool>();
                }
            }

            if (result is JToken token)
            {
                var successToken = token["success"];
                if (successToken != null && successToken.Type == JTokenType.Boolean)
                {
                    return successToken.Value<bool>();
                }
            }

            return true;
        }

        private static JObject NormalizeParameterKeys(JObject source)
        {
            if (source == null)
            {
                return new JObject();
            }

            var normalized = NormalizeObjectRecursive(source);
            NormalizationMetrics.MaybeEmitMetrics();
            return normalized;
        }

        /// <summary>
        /// Recursively normalizes a JObject, converting snake_case keys to camelCase
        /// while preserving Unity serialized field names that start with "m_".
        /// </summary>
        private static JObject NormalizeObjectRecursive(JObject source)
        {
            NormalizationMetrics.IncrementNormalizedObjects();
            var normalized = new JObject();
            var collisionTracker = new Dictionary<string, string>(); // normalizedKey -> originalKey kept

            foreach (var property in source.Properties())
            {
                string originalKey = property.Name;
                string targetKey = ShouldPreserveKey(originalKey) ? originalKey : ToCamelCase(originalKey);

                // Handle key collision: prefer keys that were already camelCase
                if (normalized.ContainsKey(targetKey))
                {
                    NormalizationMetrics.IncrementKeyCollisions();
                    string previousOriginal = collisionTracker[targetKey];
                    bool previousWasCamelCase = previousOriginal == targetKey;
                    bool currentIsCamelCase = originalKey == targetKey;

                    if (currentIsCamelCase && !previousWasCamelCase)
                    {
                        NormalizationMetrics.IncrementCamelCaseCollisionWins();
                        // Current key is explicitly camelCase, replace previous
                        McpLog.Warn($"BatchExecute key collision: '{targetKey}' from '{previousOriginal}' replaced by '{originalKey}' (explicit camelCase preferred)");
                        normalized[targetKey] = NormalizeValueRecursive(property.Value);
                        collisionTracker[targetKey] = originalKey;
                    }
                    else
                    {
                        // Keep previous, warn about dropped key
                        McpLog.Warn($"BatchExecute key collision: '{originalKey}' normalizes to '{targetKey}' but key already exists from '{previousOriginal}'");
                    }
                }
                else
                {
                    normalized[targetKey] = NormalizeValueRecursive(property.Value);
                    collisionTracker[targetKey] = originalKey;
                }
            }
            return normalized;
        }

        /// <summary>
        /// Recursively normalizes a JToken value (JObject, JArray, or scalar).
        /// </summary>
        private static JToken NormalizeValueRecursive(JToken value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is JObject obj)
            {
                return NormalizeObjectRecursive(obj);
            }

            if (value is JArray array)
            {
                var normalizedArray = new JArray();
                foreach (var item in array)
                {
                    normalizedArray.Add(NormalizeValueRecursive(item));
                }
                return normalizedArray;
            }

            // Scalar value - just clone
            return value.DeepClone();
        }

        /// <summary>
        /// Determines if a key should be preserved as-is (not converted to camelCase).
        /// Unity serialized field names starting with "m_" or shader property names
        /// starting with "_" must be preserved.
        /// </summary>
        private static bool ShouldPreserveKey(string key)
        {
            bool shouldPreserve = key != null && (
                key.StartsWith("m_", System.StringComparison.Ordinal) ||
                key.StartsWith("_", System.StringComparison.Ordinal));
            if (shouldPreserve)
            {
                NormalizationMetrics.IncrementPreservedKeys();
            }
            return shouldPreserve;
        }

        /// <summary>
        /// Exposes normalization stats for testing/inspection.
        /// </summary>
        internal static IReadOnlyDictionary<string, long> GetKeyNormalizationStats() =>
            NormalizationMetrics.GetStats();

        private static string ToCamelCase(string key) => StringCaseUtility.ToCamelCase(key);

        /// <summary>
        /// Internal class for tracking key normalization metrics.
        /// Separated to keep main BatchExecute logic focused.
        /// </summary>
        private static class NormalizationMetrics
        {
            private const int EmitInterval = 200;
            private static long s_totalNormalizeCalls;
            private static long s_totalNormalizedObjects;
            private static long s_totalPreservedKeys;
            private static long s_totalKeyCollisions;
            private static long s_totalCamelCaseCollisionWins;

            public static void IncrementNormalizedObjects() => Interlocked.Increment(ref s_totalNormalizedObjects);

            public static void IncrementPreservedKeys() => Interlocked.Increment(ref s_totalPreservedKeys);

            public static void IncrementKeyCollisions() => Interlocked.Increment(ref s_totalKeyCollisions);

            public static void IncrementCamelCaseCollisionWins() => Interlocked.Increment(ref s_totalCamelCaseCollisionWins);

            /// <summary>
            /// 
            /// </summary>
            public static void MaybeEmitMetrics()
            {
                long totalCalls = Interlocked.Increment(ref s_totalNormalizeCalls);
                if (totalCalls % EmitInterval != 0)
                {
                    return;
                }

                var stats = GetStats();
                TelemetryHelper.RecordEvent("batch_execute_key_normalization_stats", new Dictionary<string, object>
                {
                    ["normalize_calls"] = stats["normalizeCalls"],
                    ["normalized_objects"] = stats["normalizedObjects"],
                    ["preserved_keys"] = stats["preservedKeys"],
                    ["key_collisions"] = stats["keyCollisions"],
                    ["camel_case_collision_wins"] = stats["camelCaseCollisionWins"],
                });

                McpLog.Debug(
                    $"BatchExecute key normalization stats: calls={stats["normalizeCalls"]}, " +
                    $"objects={stats["normalizedObjects"]}, preserved={stats["preservedKeys"]}, " +
                    $"collisions={stats["keyCollisions"]}, camelWins={stats["camelCaseCollisionWins"]}");
            }

            public static IReadOnlyDictionary<string, long> GetStats()
            {
                return new Dictionary<string, long>
                {
                    ["normalizeCalls"] = Interlocked.Read(ref s_totalNormalizeCalls),
                    ["normalizedObjects"] = Interlocked.Read(ref s_totalNormalizedObjects),
                    ["preservedKeys"] = Interlocked.Read(ref s_totalPreservedKeys),
                    ["keyCollisions"] = Interlocked.Read(ref s_totalKeyCollisions),
                    ["camelCaseCollisionWins"] = Interlocked.Read(ref s_totalCamelCaseCollisionWins),
                };
            }
        }
    }
}
