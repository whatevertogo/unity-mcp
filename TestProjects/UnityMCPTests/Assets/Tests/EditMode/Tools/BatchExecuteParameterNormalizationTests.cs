using System.Reflection;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for parameter normalization across different MCP client JSON serialization scenarios.
    /// Covers various serialization formats from different MCP clients (Claude, Cursor, Windsurf, etc.).
    /// </summary>
    public class BatchExecuteParameterNormalizationTests
    {
        [Test]
        public void NormalizeParameterKeys_ConvertsTopLevelKeysToCamelCase()
        {
            var source = new JObject
            {
                ["search_method"] = "by_name",
                ["component_type"] = "Button"
            };

            var normalized = InvokeNormalizeParameterKeys(source);

            Assert.AreEqual("by_name", normalized["searchMethod"]?.ToString());
            Assert.AreEqual("Button", normalized["componentType"]?.ToString());
            Assert.IsNull(normalized["search_method"]);
            Assert.IsNull(normalized["component_type"]);
        }

        [Test]
        public void NormalizeParameterKeys_PreservesNestedSerializedFieldNames()
        {
            var source = new JObject
            {
                ["component_type"] = "Button",
                ["value"] = new JObject
                {
                    ["m_PersistentCalls"] = new JObject
                    {
                        ["m_Calls"] = new JArray()
                    },
                    ["some_field"] = 123
                }
            };

            var normalized = InvokeNormalizeParameterKeys(source);
            var valueObj = normalized["value"] as JObject;

            Assert.AreEqual("Button", normalized["componentType"]?.ToString());
            Assert.IsNotNull(valueObj);
            // m_ prefixed keys should be preserved at all levels
            Assert.IsNotNull(valueObj["m_PersistentCalls"], "m_PersistentCalls should be preserved");
            Assert.IsNull(valueObj["mPersistentCalls"], "m_PersistentCalls should NOT be converted to mPersistentCalls");
            var persistentCalls = valueObj["m_PersistentCalls"] as JObject;
            Assert.IsNotNull(persistentCalls);
            Assert.IsNotNull(persistentCalls["m_Calls"], "m_Calls should be preserved");
            // Regular snake_case keys should still be converted
            Assert.IsNull(valueObj["some_field"], "some_field should be converted");
            Assert.AreEqual(123, valueObj["someField"]?.Value<int>(), "some_field should be converted to someField");
        }

        [Test]
        public void NormalizeParameterKeys_PreservesMUnderscoreAtTopLevel()
        {
            var source = new JObject
            {
                ["m_SerializedField"] = "value1",
                ["m_AnotherField"] = new JObject { ["nested"] = 42 },
                ["regular_field"] = "value2"
            };

            var normalized = InvokeNormalizeParameterKeys(source);

            // m_ prefixed keys at top level should be preserved
            Assert.AreEqual("value1", normalized["m_SerializedField"]?.ToString());
            Assert.IsNull(normalized["mSerializedField"]);
            Assert.IsNotNull(normalized["m_AnotherField"]);
            Assert.IsNull(normalized["mAnotherField"]);
            // Regular snake_case should be converted
            Assert.AreEqual("value2", normalized["regularField"]?.ToString());
            Assert.IsNull(normalized["regular_field"]);
        }

        [Test]
        public void NormalizeParameterKeys_ConvertsNestedSnakeCaseKeys()
        {
            var source = new JObject
            {
                ["outer_key"] = new JObject
                {
                    ["inner_key"] = "value",
                    ["another_nested"] = new JObject
                    {
                        ["deep_key"] = 123
                    }
                }
            };

            var normalized = InvokeNormalizeParameterKeys(source);
            var outerObj = normalized["outerKey"] as JObject;

            Assert.IsNotNull(outerObj);
            Assert.IsNull(normalized["outer_key"]);
            Assert.AreEqual("value", outerObj["innerKey"]?.ToString());
            Assert.IsNull(outerObj["inner_key"]);

            var nestedObj = outerObj["anotherNested"] as JObject;
            Assert.IsNotNull(nestedObj);
            Assert.IsNull(outerObj["another_nested"]);
            Assert.AreEqual(123, nestedObj["deepKey"]?.Value<int>());
            Assert.IsNull(nestedObj["deep_key"]);
        }

        [Test]
        public void NormalizeParameterKeys_PreservesSerializedKeysInsideArrays()
        {
            var source = new JObject
            {
                ["events"] = new JArray
                {
                    new JObject
                    {
                        ["m_PersistentCalls"] = new JArray(),
                        ["event_name"] = "click"
                    },
                    new JObject
                    {
                        ["m_Target"] = "targetObject",
                        ["callback_id"] = 1
                    }
                }
            };

            var normalized = InvokeNormalizeParameterKeys(source);
            var events = normalized["events"] as JArray;

            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            // First array element
            var firstEvent = events[0] as JObject;
            Assert.IsNotNull(firstEvent);
            Assert.IsNotNull(firstEvent["m_PersistentCalls"], "m_PersistentCalls in array should be preserved");
            Assert.IsNull(firstEvent["mPersistentCalls"]);
            Assert.AreEqual("click", firstEvent["eventName"]?.ToString());
            Assert.IsNull(firstEvent["event_name"]);

            // Second array element
            var secondEvent = events[1] as JObject;
            Assert.IsNotNull(secondEvent);
            Assert.IsNotNull(secondEvent["m_Target"], "m_Target in array should be preserved");
            Assert.IsNull(secondEvent["mTarget"]);
            Assert.AreEqual(1, secondEvent["callbackId"]?.Value<int>());
            Assert.IsNull(secondEvent["callback_id"]);
        }

        [Test]
        public void NormalizeParameterKeys_PreservesUnderscorePrefixedKeys()
        {
            var source = new JObject
            {
                ["_TopLevel"] = "keep",
                ["properties"] = new JObject
                {
                    ["_BaseColor"] = new JArray(1, 0, 0, 1),
                    ["_MainTex"] = "Assets/Textures/T.png",
                    ["some_field"] = 7
                }
            };

            var normalized = InvokeNormalizeParameterKeys(source);
            var properties = normalized["properties"] as JObject;

            Assert.AreEqual("keep", normalized["_TopLevel"]?.ToString());
            Assert.IsNull(normalized["TopLevel"]);
            Assert.IsNotNull(properties);
            Assert.IsNotNull(properties["_BaseColor"], "_BaseColor should be preserved");
            Assert.IsNull(properties["BaseColor"]);
            Assert.AreEqual("Assets/Textures/T.png", properties["_MainTex"]?.ToString());
            Assert.IsNull(properties["MainTex"]);
            Assert.AreEqual(7, properties["someField"]?.Value<int>());
            Assert.IsNull(properties["some_field"]);
        }

        [Test]
        public void NormalizeParameterKeys_MixedNestedKeys_PreservesSerializedAndConvertsSnakeCase()
        {
            var source = new JObject
            {
                ["my_component"] = new JObject
                {
                    ["enabled"] = true,
                    ["m_Script"] = new JObject
                    {
                        ["fileID"] = 123
                    }
                }
            };

            var normalized = InvokeNormalizeParameterKeys(source);
            var myComponent = normalized["myComponent"] as JObject;

            Assert.IsNotNull(myComponent);
            Assert.IsNull(normalized["my_component"]);
            Assert.AreEqual(true, myComponent["enabled"]?.Value<bool>());
            Assert.IsNotNull(myComponent["m_Script"], "m_Script should be preserved");
            Assert.IsNull(myComponent["mScript"]);
            var mScript = myComponent["m_Script"] as JObject;
            Assert.IsNotNull(mScript);
            Assert.AreEqual(123, mScript["fileID"]?.Value<int>());
        }

        [Test]
        public void NormalizeParameterKeys_CollisionPrefersExplicitCamelCase()
        {
            // When two keys normalize to the same value, explicit camelCase should win
            var source = new JObject
            {
                ["some_field"] = "snake_value",
                ["someField"] = "camel_value"
            };

            var normalized = InvokeNormalizeParameterKeys(source);

            // Both keys normalize to "someField", explicit camelCase should win
            Assert.AreEqual("camel_value", normalized["someField"]?.ToString());
            Assert.AreEqual(1, ((JObject)normalized).Count); // Only one key
        }

        [Test]
        public void NormalizeParameterKeys_CollisionSnakeCaseDroppedWhenExplicitCamelCaseExists()
        {
            // When explicit camelCase exists, snake_case should be dropped
            var source = new JObject
            {
                ["search_method"] = "snake_value",
                ["searchMethod"] = "camel_value"
            };

            var normalized = InvokeNormalizeParameterKeys(source);

            // Explicit camelCase should win
            Assert.AreEqual("camel_value", normalized["searchMethod"]?.ToString());
            Assert.IsNull(normalized["search_method"]);
            Assert.IsNull(normalized["search_method"]);
            Assert.AreEqual(1, ((JObject)normalized).Count);
        }

        [Test]
        public void NormalizeParameterKeys_CollisionWhenBothAreSnakeCaseKeepsFirst()
        {
            // When both keys are snake_case (converted to same camelCase), first is kept
            var source = new JObject
            {
                ["my_field"] = "first_value",
                ["my_field"] = "second_value" // Duplicate key (invalid JSON but possible via programmatic construction)
            };

            var normalized = InvokeNormalizeParameterKeys(source);

            // First value should be kept (JObject keeps last value for duplicate keys)
            Assert.AreEqual("second_value", normalized["myField"]?.ToString());
        }

        [Test]
        public void NormalizeParameterKeys_PreservedKeyCollision_KeepsFirst()
        {
            // When m_ prefixed key collides with regular key, m_ key wins (preserved)
            var source = new JObject
            {
                ["my_field"] = "regular_value",
                ["m_myField"] = "preserved_value"
            };

            var normalized = InvokeNormalizeParameterKeys(source);

            // m_ prefixed key should be preserved as-is, so it won't collide
            Assert.AreEqual("regular_value", normalized["myField"]?.ToString());
            Assert.AreEqual("preserved_value", normalized["m_myField"]?.ToString());
            Assert.AreEqual(2, ((JObject)normalized).Count);
        }

        [Test]
        public void GetKeyCollisionBehavior_ReturnsWarnByDefault()
        {
            var method = typeof(BatchExecute).GetMethod(
                "GetKeyCollisionBehavior",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.IsNotNull(method, "Failed to locate BatchExecute.GetKeyCollisionBehavior via reflection.");

            var result = method.Invoke(null, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, (int)result, "Default should be Warn (0)");
        }

        [Test]
        public void GetTelemetryEmitInterval_ReturnsDefault()
        {
            var method = typeof(BatchExecute).GetMethod(
                "GetTelemetryEmitInterval",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.IsNotNull(method, "Failed to locate BatchExecute.GetTelemetryEmitInterval via reflection.");

            var result = method.Invoke(null, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(200, (int)result, "Default should be 200");
        }

        [Test]
        public void GetTelemetrySampleRate_ReturnsDefault()
        {
            var method = typeof(BatchExecute).GetMethod(
                "GetTelemetrySampleRate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.IsNotNull(method, "Failed to locate BatchExecute.GetTelemetrySampleRate via reflection.");

            var result = method.Invoke(null, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(1.0, (double)result, 0.001, "Default should be 1.0");
        }

        [Test]
        public void GetKeyNormalizationStats_ReturnsCorrectStats()
        {
            // Normalize a few objects to generate stats
            InvokeNormalizeParameterKeys(new JObject { ["a"] = 1, ["b"] = 2 });
            InvokeNormalizeParameterKeys(new JObject { ["m_preserved"] = 3, ["c"] = 4 });

            var stats = BatchExecute.GetKeyNormalizationStats();

            Assert.IsNotNull(stats);
            Assert.Greater(stats["normalizeCalls"], 0);
            Assert.Greater(stats["normalizedObjects"], 0);
            Assert.Greater(stats["preservedKeys"], 0);
        }

        #region MCP Client JSON Serialization Scenarios

        /// <summary>
        /// Tests scenario where MCP client sends snake_case parameters (common in Python-based clients).
        /// </summary>
        [Test]
        public void MCPClient_PythonStyle_SnakeCaseParameters()
        {
            var source = new JObject
            {
                ["search_method"] = "by_name",
                ["component_type"] = "Button",
                ["search_value"] = "MainCamera"
            };

            var normalized = InvokeNormalizeParameterKeys(source);

            Assert.AreEqual("by_name", normalized["searchMethod"]?.ToString());
            Assert.AreEqual("Button", normalized["componentType"]?.ToString());
            Assert.AreEqual("MainCamera", normalized["searchValue"]?.ToString());
            Assert.IsNull(normalized["search_method"]);
            Assert.IsNull(normalized["component_type"]);
            Assert.IsNull(normalized["search_value"]);
        }

        /// <summary>
        /// Tests scenario where MCP client sends camelCase parameters (common in JavaScript/TypeScript clients).
        /// </summary>
        [Test]
        public void MCPClient_JavaScriptStyle_CamelCaseParameters()
        {
            var source = new JObject
            {
                ["searchMethod"] = "by_name",
                ["componentType"] = "Button",
                ["searchValue"] = "MainCamera"
            };

            var normalized = InvokeNormalizeParameterKeys(source);

            Assert.AreEqual("by_name", normalized["searchMethod"]?.ToString());
            Assert.AreEqual("Button", normalized["componentType"]?.ToString());
            Assert.AreEqual("MainCamera", normalized["searchValue"]?.ToString());
        }

        /// <summary>
        /// Tests scenario with Unity serialized fields (m_ prefix) from any client.
        /// </summary>
        [Test]
        public void MCPClient_UnitySerializedFields_PreservesMPrefix()
        {
            var source = new JObject
            {
                ["name"] = "TestObject",
                ["m_Name"] = "SerializedName",
                ["transform"] = new JObject
                {
                    ["m_LocalPosition"] = new JArray(1, 2, 3),
                    ["m_LocalRotation"] = new JArray(0, 0, 0, 1),
                    ["local_scale"] = new JArray(1, 1, 1)
                }
            };

            var normalized = InvokeNormalizeParameterKeys(source);
            var transform = normalized["transform"] as JObject;

            Assert.IsNotNull(transform);
            Assert.IsNotNull(transform["m_LocalPosition"], "m_ prefixed keys should be preserved");
            Assert.IsNull(transform["mLocalPosition"]);
            Assert.IsNotNull(transform["m_LocalRotation"]);
            Assert.IsNull(transform["mLocalRotation"]);
            // Regular snake_case should still be converted
            Assert.IsNotNull(transform["localScale"]);
            Assert.IsNull(transform["local_scale"]);
        }

        /// <summary>
        /// Tests scenario with shader properties (_ prefix) from any client.
        /// </summary>
        [Test]
        public void MCPClient_ShaderProperties_PreservesUnderscorePrefix()
        {
            var source = new JObject
            {
                ["material_name"] = "TestMaterial",
                ["properties"] = new JObject
                {
                    ["_MainTex"] = "Assets/Textures/T.png",
                    ["_BaseColor"] = new JArray(1, 0, 0, 1),
                    ["_BumpMap"] = "Assets/Textures/Normal.png",
                    ["shader_keywords"] = new JArray("DIRECTIONAL_LIGHT")
                }
            };

            var normalized = InvokeNormalizeParameterKeys(source);
            var properties = normalized["properties"] as JObject;

            Assert.IsNotNull(properties);
            Assert.IsNotNull(properties["_MainTex"], "_ prefixed keys should be preserved");
            Assert.IsNull(properties["MainTex"]);
            Assert.IsNotNull(properties["_BaseColor"]);
            Assert.IsNull(properties["BaseColor"]);
            Assert.IsNotNull(properties["_BumpMap"]);
            Assert.IsNull(properties["BumpMap"]);
            // Regular snake_case should still be converted
            Assert.IsNotNull(properties["shaderKeywords"]);
            Assert.IsNull(properties["shader_keywords"]);
        }

        /// <summary>
        /// Tests scenario with deeply nested objects from any client.
        /// </summary>
        [Test]
        public void MCPClient_DeeplyNestedObjects_ConvertsAllLevels()
        {
            var source = new JObject
            {
                ["outer_key"] = new JObject
                {
                    ["middle_key"] = new JObject
                    {
                        ["deep_key"] = new JObject
                        {
                            ["deeper_key"] = "value"
                        }
                    }
                }
            };

            var normalized = InvokeNormalizeParameterKeys(source);
            var outer = normalized["outerKey"] as JObject;
            var middle = outer["middleKey"] as JObject;
            var deep = middle["deepKey"] as JObject;

            Assert.IsNotNull(outer);
            Assert.IsNotNull(middle);
            Assert.IsNotNull(deep);
            Assert.AreEqual("value", deep["deeperKey"]?.ToString());
            Assert.IsNull(deep["deeper_key"]);
        }

        /// <summary>
        /// Tests scenario with arrays of objects from any client.
        /// </summary>
        [Test]
        public void MCPClient_ArrayOfObjects_NormalizesEachElement()
        {
            var source = new JObject
            {
                ["components"] = new JArray
                {
                    new JObject
                    {
                        ["component_type"] = "MeshRenderer",
                        ["material_name"] = "Default"
                    },
                    new JObject
                    {
                        ["component_type"] = "BoxCollider",
                        ["is_trigger"] = true
                    }
                }
            };

            var normalized = InvokeNormalizeParameterKeys(source);
            var components = normalized["components"] as JArray;

            Assert.IsNotNull(components);
            Assert.AreEqual(2, components.Count);

            var first = components[0] as JObject;
            Assert.IsNotNull(first);
            Assert.AreEqual("MeshRenderer", first["componentType"]?.ToString());
            Assert.AreEqual("Default", first["materialName"]?.ToString());

            var second = components[1] as JObject;
            Assert.IsNotNull(second);
            Assert.AreEqual("BoxCollider", second["componentType"]?.ToString());
            Assert.AreEqual(true, second["isTrigger"]?.Value<bool>());
        }

        /// <summary>
        /// Tests scenario with mixed serialization formats (some camelCase, some snake_case).
        /// </summary>
        [Test]
        public void MCPClient_MixedSerialization_HandlesCorrectly()
        {
            var source = new JObject
            {
                ["searchMethod"] = "by_name", // Already camelCase
                ["search_value"] = "Player", // snake_case
                ["componentType"] = "Rigidbody", // Already camelCase
                ["use_gravity"] = true, // snake_case
                ["constraints"] = new JObject // Nested object with snake_case
                {
                    ["freeze_position_x"] = true,
                    ["freeze_rotation_z"] = false
                }
            };

            var normalized = InvokeNormalizeParameterKeys(source);
            var constraints = normalized["constraints"] as JObject;

            Assert.IsNotNull(constraints);
            Assert.AreEqual("by_name", normalized["searchMethod"]?.ToString());
            Assert.AreEqual("Player", normalized["searchValue"]?.ToString());
            Assert.AreEqual("Rigidbody", normalized["componentType"]?.ToString());
            Assert.AreEqual(true, normalized["useGravity"]?.Value<bool>());
            Assert.AreEqual(true, constraints["freezePositionX"]?.Value<bool>());
            Assert.AreEqual(false, constraints["freezeRotationZ"]?.Value<bool>());
        }

        /// <summary>
        /// Tests scenario with empty/null values from any client.
        /// </summary>
        [Test]
        public void MCPClient_EmptyOrNullValues_PreservesKeys()
        {
            var source = new JObject
            {
                ["name"] = (string)null,
                ["tag"] = "",
                ["layer"] = "Default"
            };

            var normalized = InvokeNormalizeParameterKeys(source);

            Assert.IsNull(normalized["name"]);
            Assert.AreEqual("", normalized["tag"]?.ToString());
            Assert.AreEqual("Default", normalized["layer"]?.ToString());
        }

        /// <summary>
        /// Tests scenario with numeric and boolean values from any client.
        /// </summary>
        [Test]
        public void MCPClient_NumericAndBooleanValues_PreservesCorrectly()
        {
            var source = new JObject
            {
                ["position_x"] = 1.5,
                ["position_y"] = 2.5,
                ["position_z"] = 3.5,
                ["is_active"] = true,
                ["use_physics"] = false,
                ["count"] = 42
            };

            var normalized = InvokeNormalizeParameterKeys(source);

            Assert.AreEqual(1.5, normalized["positionX"]?.Value<double>());
            Assert.AreEqual(2.5, normalized["positionY"]?.Value<double>());
            Assert.AreEqual(3.5, normalized["positionZ"]?.Value<double>());
            Assert.AreEqual(true, normalized["isActive"]?.Value<bool>());
            Assert.AreEqual(false, normalized["usePhysics"]?.Value<bool>());
            Assert.AreEqual(42, normalized["count"]?.Value<int>());
        }

        #endregion

        private static JObject InvokeNormalizeParameterKeys(JObject source)
        {
            var method = typeof(BatchExecute).GetMethod(
                "NormalizeParameterKeys",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method, "Failed to locate BatchExecute.NormalizeParameterKeys via reflection.");

            var result = method.Invoke(null, new object[] { source }) as JObject;
            Assert.IsNotNull(result, "NormalizeParameterKeys returned null.");

            return result;
        }
    }
}
