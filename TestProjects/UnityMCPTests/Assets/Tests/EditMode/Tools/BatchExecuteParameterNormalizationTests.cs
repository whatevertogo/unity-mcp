using System.Reflection;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Tools
{
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
