using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 设备租约键解析（与 <see cref="SequenceExecutor"/> 内 CollectRequiredDevices 语义一致）。
    /// </summary>
    internal static class DeviceKeyResolver
    {
        /// <summary>Parameters 中的控制面键，不得当作设备租约键（如 Condition=false）。</summary>
        private static readonly HashSet<string> NonDeviceParameterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Condition",
            "TimeoutMs",
            "TimeoutAction",
            "RetryCount",
            "RetryDelayMs",
            "StartDelayMs",
            "StopByFail"
        };

        public static HashSet<string> CollectKeysFromStepTree(
            IEnumerable<StepConfig> roots,
            IDictionary<string, object> deviceRoles)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (roots == null)
            {
                return keys;
            }

            foreach (var root in roots)
            {
                CollectFromStep(root, deviceRoles, keys);
            }

            return keys;
        }

        public static void CollectFromStep(StepConfig step, IDictionary<string, object> deviceRoles, ISet<string> keys)
        {
            if (step == null || !step.Enable || keys == null)
            {
                return;
            }

            AddTargetKey(step.Target, deviceRoles, keys);

            if (step.AdditionalTargets != null)
            {
                foreach (var additionalTarget in step.AdditionalTargets)
                {
                    AddTargetKey(additionalTarget, deviceRoles, keys);
                }
            }

            if (step.SubSteps != null)
            {
                foreach (var sub in step.SubSteps)
                {
                    CollectFromStep(sub, deviceRoles, keys);
                }
            }

            if (step.Parameters != null)
            {
                foreach (var kvp in step.Parameters)
                {
                    if (NonDeviceParameterKeys.Contains(kvp.Key))
                    {
                        continue;
                    }

                    CollectNestedTargets(kvp.Value, deviceRoles, keys);
                }
            }
        }

        private static void AddTargetKey(string target, IDictionary<string, object> deviceRoles, ISet<string> keys)
        {
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            if (deviceRoles != null && deviceRoles.TryGetValue(target, out var mapped))
            {
                keys.Add(mapped?.ToString() ?? target);
            }
            else
            {
                keys.Add(target);
            }
        }

        private static void CollectNestedTargets(object obj, IDictionary<string, object> deviceRoles, ISet<string> keys)
        {
            if (obj == null)
            {
                return;
            }

            // 裸字符串不是设备键；仅嵌套结构中的 Target/Device 字段才解析为租约键。
            if (obj is string)
            {
                return;
            }

            if (obj is JObject jobj)
            {
                foreach (var prop in jobj.Properties())
                {
                    if (string.Equals(prop.Name, "Target", StringComparison.OrdinalIgnoreCase)
                        && (prop.Value.Type == JTokenType.String || prop.Value.Type == JTokenType.Raw))
                    {
                        AddTargetKey(prop.Value.ToString(), deviceRoles, keys);
                    }

                    CollectNestedTargets(prop.Value, deviceRoles, keys);
                }

                return;
            }

            if (obj is JArray jarr)
            {
                foreach (var item in jarr)
                {
                    CollectNestedTargets(item, deviceRoles, keys);
                }

                return;
            }

            if (obj is IDictionary<string, object> dict)
            {
                foreach (var kvp in dict)
                {
                    if (string.Equals(kvp.Key, "Target", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(kvp.Key, "Device", StringComparison.OrdinalIgnoreCase))
                    {
                        AddTargetKey(kvp.Value?.ToString(), deviceRoles, keys);
                    }

                    CollectNestedTargets(kvp.Value, deviceRoles, keys);
                }

                return;
            }

            if (obj is IEnumerable<object> list)
            {
                foreach (var item in list)
                {
                    CollectNestedTargets(item, deviceRoles, keys);
                }
            }
        }
    }
}
