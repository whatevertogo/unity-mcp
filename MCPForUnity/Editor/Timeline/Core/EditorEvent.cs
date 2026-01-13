using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MCPForUnity.Editor.Timeline.Core
{
    /// <summary>
    /// Immutable class representing a single editor event.
    /// This is the "bedrock" layer - once written, never modified.
    ///
    /// Payload serialization constraints:
    /// - Only JSON-serializable types are allowed: string, number (int/long/float/double/decimal),
    ///   bool, null, array of these types, or Dictionary<string, object> with these value types.
    /// - Unsupported types (UnityEngine.Object, MonoBehaviour, etc.) are logged and skipped.
    /// </summary>
    public sealed class EditorEvent : IEquatable<EditorEvent>
    {
        /// <summary>
        /// Monotonically increasing sequence number for ordering.
        /// </summary>
        public long Sequence { get; }

        /// <summary>
        /// UTC timestamp in milliseconds since Unix epoch.
        /// </summary>
        public long TimestampUnixMs { get; }

        /// <summary>
        /// Event type identifier (e.g., "GameObjectCreated", "ComponentAdded").
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Target identifier (instance ID, asset GUID, or file path).
        /// </summary>
        public string TargetId { get; }

        /// <summary>
        /// Event payload containing additional context data.
        /// All values are guaranteed to be JSON-serializable.
        /// </summary>
        public IReadOnlyDictionary<string, object> Payload { get; }

        public EditorEvent(
            long sequence,
            long timestampUnixMs,
            string type,
            string targetId,
            IReadOnlyDictionary<string, object> payload)
        {
            Sequence = sequence;
            TimestampUnixMs = timestampUnixMs;
            Type = type ?? throw new ArgumentNullException(nameof(type));
            TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));

            // Validate and sanitize payload to ensure JSON-serializable types
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            Payload = SanitizePayload(payload, type);
        }

        /// <summary>
        /// Validate and sanitize payload values to ensure JSON serializability.
        /// Converts values to safe types and logs warnings for unsupported types.
        /// </summary>
        private static Dictionary<string, object> SanitizePayload(
            IReadOnlyDictionary<string, object> payload,
            string eventType)
        {
            var sanitized = new Dictionary<string, object>();

            foreach (var kvp in payload)
            {
                var value = SanitizeValue(kvp.Value, kvp.Key, eventType);
                if (value != null || kvp.Value == null)
                {
                    // Only add if not filtered out (null values are allowed)
                    sanitized[kvp.Key] = value;
                }
            }

            return sanitized;
        }

        /// <summary>
        /// Recursively validate and sanitize a single value.
        /// Returns null for unsupported types (which will be filtered out).
        /// </summary>
        private static object SanitizeValue(object value, string key, string eventType)
        {
            if (value == null)
                return null;

            // Primitive JSON-serializable types
            if (value is string || value is bool)
                return value;

            // Numeric types - convert to consistent types
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is float f) return f;
            if (value is double d) return d;
            if (value is decimal m) return m;
            if (value is uint ui) return ui;
            if (value is ulong ul) return ul;
            if (value is short sh) return sh;
            if (value is ushort ush) return ush;
            if (value is byte b) return b;
            if (value is sbyte sb) return sb;
            if (value is char c) return c.ToString();  // Char as string

            // Arrays - handle native arrays (int[], string[], etc.)
            if (value.GetType().IsArray)
            {
                return SanitizeArray((Array)value, key, eventType);
            }

            // Generic collections - use non-generic interface for broader compatibility
            // This handles List<T>, IEnumerable<T>, HashSet<T>, etc. with any element type
            if (value is IEnumerable enumerable && !(value is string) && !(value is IDictionary))
            {
                return SanitizeEnumerable(enumerable, key, eventType);
            }

            // Dictionaries - use non-generic interface for broader compatibility
            // This handles Dictionary<K,V> with any value type
            if (value is IDictionary dict)
            {
                return SanitizeDictionary(dict, key, eventType);
            }

            // Unsupported type - log warning and filter out
            UnityEngine.Debug.LogWarning(
                $"[EditorEvent] Unsupported payload type '{value.GetType().Name}' " +
                $"for key '{key}' in event '{eventType}'. Value will be excluded from payload. " +
                $"Supported types: string, number, bool, null, array, List, Dictionary.");

            return null;  // Filter out unsupported types
        }

        /// <summary>
        /// Sanitize a native array.
        /// </summary>
        private static object SanitizeArray(Array array, string key, string eventType)
        {
            var list = new List<object>(array.Length);
            foreach (var item in array)
            {
                var sanitized = SanitizeValue(item, key, eventType);
                if (sanitized != null || item == null)
                {
                    list.Add(sanitized);
                }
            }
            return list;
        }

        /// <summary>
        /// Sanitize a generic IEnumerable (List<T>, IEnumerable<T>, etc.)
        /// Uses non-generic interface to handle any element type.
        /// </summary>
        private static object SanitizeEnumerable(IEnumerable enumerable, string key, string eventType)
        {
            var list = new List<object>();
            foreach (var item in enumerable)
            {
                var sanitized = SanitizeValue(item, key, eventType);
                if (sanitized != null || item == null)
                {
                    list.Add(sanitized);
                }
            }
            return list;
        }

        /// <summary>
        /// Sanitize a generic IDictionary (Dictionary<K,V>, etc.)
        /// Uses non-generic interface to handle any key/value types.
        /// Only string keys are supported; other key types are skipped with warning.
        /// </summary>
        private static object SanitizeDictionary(IDictionary dict, string key, string eventType)
        {
            var result = new Dictionary<string, object>();

            foreach (DictionaryEntry entry in dict)
            {
                // Only support string keys
                if (entry.Key is string stringKey)
                {
                    var sanitizedValue = SanitizeValue(entry.Value, stringKey, eventType);
                    if (sanitizedValue != null || entry.Value == null)
                    {
                        result[stringKey] = sanitizedValue;
                    }
                }
                else
                {
                    UnityEngine.Debug.LogWarning(
                        $"[EditorEvent] Dictionary key type '{entry.Key?.GetType().Name}' " +
                        $"is not supported. Only string keys are supported. Key will be skipped.");
                }
            }

            return result;
        }

        // ========================================================================
        // FORBIDDEN FIELDS - Do NOT add these properties to EditorEvent:
        // - Importance: Calculate at query time, not stored
        // - Source/AI/Human flags: Use Context layer (ContextMapping side-table)
        // - SessionId: Use Context layer
        // - _ctx: Use Context layer
        // These are intentionally omitted to keep the event layer pure.
        // ========================================================================

        public bool Equals(EditorEvent other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Sequence == other.Sequence
                && TimestampUnixMs == other.TimestampUnixMs
                && Type == other.Type
                && TargetId == other.TargetId;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as EditorEvent);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Sequence, TimestampUnixMs, Type, TargetId);
        }

        public static bool operator ==(EditorEvent left, EditorEvent right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(EditorEvent left, EditorEvent right)
        {
            return !Equals(left, right);
        }
    }
}
