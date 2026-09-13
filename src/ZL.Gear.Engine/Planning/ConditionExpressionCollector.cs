using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Planning
{
    /// <summary>
    /// 从 Plan 步骤树收集全部布尔条件表达式（Parameters.Condition + DynamicFlow WorkflowDefinition 内嵌 Condition）。
    /// </summary>
    public static class ConditionExpressionCollector
    {
        public static IEnumerable<(string StepKey, string Expression, bool IsWaitUntilCondition)> CollectFromSteps(IEnumerable<StepConfig> roots)
        {
            if (roots == null)
            {
                yield break;
            }

            foreach (var step in roots.SelectMany(r => StepKit.FlattenSteps(r)).Where(s => s.Enable))
            {
                if (step.Parameters == null)
                {
                    continue;
                }

                foreach (var pair in CollectFromObject(step.Parameters, step.StepKey))
                {
                    yield return pair;
                }
            }
        }

        private static IEnumerable<(string StepKey, string Expression, bool IsWaitUntilCondition)> CollectFromObject(object obj, string stepKey, bool isWaitUntil = false)
        {
            if (obj == null)
            {
                yield break;
            }

            if (obj is string str)
            {
                yield break;
            }

            if (obj is JObject jobj)
            {
                foreach (var pair in CollectFromJObject(jobj, stepKey, isWaitUntil))
                {
                    yield return pair;
                }

                yield break;
            }

            if (obj is JArray jarr)
            {
                foreach (var item in jarr)
                {
                    foreach (var pair in CollectFromObject(item, stepKey, isWaitUntil))
                    {
                        yield return pair;
                    }
                }

                yield break;
            }

            if (obj is IDictionary<string, object> dict)
            {
                // WorkflowNode 类型字段为 WaitUntil 时，其 Condition 属于执行期轮询条件，需单独标记
                if (dict.TryGetValue("Type", out var typeObj) && typeObj != null && string.Equals(typeObj.ToString(), "WaitUntil", StringComparison.OrdinalIgnoreCase))
                {
                    isWaitUntil = true;
                }

                foreach (var kvp in dict)
                {
                    if (string.Equals(kvp.Key, "Condition", StringComparison.OrdinalIgnoreCase)
                        && kvp.Value != null)
                    {
                        var text = kvp.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            yield return (stepKey, text, isWaitUntil);
                        }
                    }

                    foreach (var pair in CollectFromObject(kvp.Value, stepKey, isWaitUntil))
                    {
                        yield return pair;
                    }
                }

                yield break;
            }

            if (obj is IEnumerable<object> list && !(obj is string))
            {
                foreach (var item in list)
                {
                    foreach (var pair in CollectFromObject(item, stepKey, isWaitUntil))
                    {
                        yield return pair;
                    }
                }
            }
        }

        private static IEnumerable<(string StepKey, string Expression, bool IsWaitUntilCondition)> CollectFromJObject(JObject jobj, string stepKey, bool isWaitUntil = false)
        {
            // WorkflowNode 类型字段为 WaitUntil 时，其 Condition 属于执行期轮询条件，需单独标记
            var typeToken = jobj["Type"];
            if (typeToken != null && string.Equals(typeToken.ToString(), "WaitUntil", StringComparison.OrdinalIgnoreCase))
            {
                isWaitUntil = true;
            }

            foreach (var prop in jobj.Properties())
            {
                if (string.Equals(prop.Name, "Condition", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.Type == JTokenType.String)
                {
                    var text = prop.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        yield return (stepKey, text, isWaitUntil);
                    }
                }

                foreach (var pair in CollectFromObject(prop.Value, stepKey, isWaitUntil))
                {
                    yield return pair;
                }
            }
        }
    }
}
