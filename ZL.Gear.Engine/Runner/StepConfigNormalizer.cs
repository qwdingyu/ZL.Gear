using System;
using System.Collections.Generic;
using System.Linq;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Engine.Runner
{
    // 建议放在一个专门的 "Normalization" 或 "Compatibility" 文件夹下
    public static class StepConfigNormalizer
    {
        /// <summary>
        /// 检查 StepConfig，如果它使用了旧的'|'分隔符格式定义期望结果，
        /// 则将其解析并转换为标准的 ExpectedResults 列表格式。
        /// 这个方法是幂等的，多次调用无副作用。
        /// </summary>
        /// <param name="step">要进行归一化的步骤配置</param>
        public static void Normalize(StepConfig step, IDictionary<string, object> deviceRoles)
        {
            // 如果步骤为 null，则直接返回，防止异常
            if (step == null) return;
            var TargetDict = new Dictionary<string, object>();
            foreach (var key in step.AdditionalTargets)
            {
                if (deviceRoles.ContainsKey(key) && !TargetDict.ContainsKey(key))
                {
                    if (deviceRoles.TryGetValue(key, out object val))
                        TargetDict.Add(key, val);
                }
            }
            step.TargetDict = TargetDict;
            var parameters = step.Parameters ?? new Dictionary<string, object>();
            int timeoutMs = step.TimeoutMs;
            // 确保时长不超过总超时
            if (parameters.ContainsKey("DurationMs") && parameters.Get<int>("DurationMs") > timeoutMs)
            {
                parameters["DurationMs"] = timeoutMs > 500 ? timeoutMs - 500 : 0;
            }
            if (!parameters.ContainsKey("DurationMs"))
            {
                parameters["DurationMs"] = timeoutMs > 500 ? timeoutMs - 500 : 0;
            }

            // 如果是新格式，则无需转换
            if (step.ExpectedResults != null && step.ExpectedResults.Any())
            {
                // 即便是新格式，也要继续处理它的子任务
            }
            else
            {
                if (step.Parameters.ContainsKey("LCL") && step.Parameters.ContainsKey("UCL"))
                {
                    step.ExpectedResults = new List<ExpectedSpec>();
                    try
                    {
                        var keyStr = step.MeasurementKey;
                        if (!string.IsNullOrEmpty(keyStr))
                        {
                            var lclStr = parameters.Get<string>("LCL");
                            var uclStr = parameters.Get<string>("UCL");
                            var offsetStr = parameters.Get<string>("Offset");
                            var unit = parameters.Get<string>("Unit");
                            if (!string.IsNullOrWhiteSpace(lclStr) || !string.IsNullOrWhiteSpace(uclStr) || !string.IsNullOrWhiteSpace(offsetStr))
                            {
                                NormalizeMultiValue(step, keyStr, lclStr, uclStr, offsetStr, unit);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"在规范化步骤 '{step.StepName}' 的期望结果时发生错误。", ex);
                    }
                }
            }

            // --- 步骤 2: 【关键新增】递归处理所有并发子步骤 ---
            if (step.SubSteps != null && step.SubSteps.Any())
            {
                foreach (var subStep in step.SubSteps)
                {
                    // 对每个子步骤调用自身，实现递归
                    Normalize(subStep, deviceRoles);
                }
            }
        }

        /// <summary>
        /// 处理多值情况，填充 step.ExpectedResults。
        /// </summary>
        private static void NormalizeMultiValue(StepConfig step, string keyStr, string lclStr, string uclStr, string offsetStr, string unit)
        {
            step.ExpectedResults = new List<ExpectedSpec>();
            var keys = keyStr.Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries);
            var lclStrings = lclStr.Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries);
            var uclStrings = uclStr.Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (keys.Length != lclStrings.Length || keys.Length != uclStrings.Length)
            {
                if (lclStrings.Length == uclStrings.Length && lclStrings.Length == 1 && keys.Length > 1)
                {
                    keys = new string[] { keys[0] };
                }
                else
                {
                    throw new System.ArgumentException($"参数数量不匹配。'channel' ({keys.Length}个)、'LCL' ({lclStrings.Length}个) 和 'UCL' ({uclStrings.Length}个) 的项目数量必须完全相同。");
                }
            }

            string[] offsetStrings = new string[] { "0" };
            offsetStrings = string.IsNullOrEmpty(offsetStr) ? Enumerable.Repeat("0", keys.Length).ToArray() : offsetStr.Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < keys.Length; i++)
            {
                var spec = new ExpectedSpec { Key = keys[i].Trim(), Mode = "range", Unit = unit, };
                if (double.TryParse(lclStrings[i], out var lclValue))
                {
                    spec.LCL = lclValue;
                }
                else
                {
                    throw new System.ArgumentException($"无法解析通道 '{spec.Key}' 的LCL值: '{lclStrings[i]}'");
                }
                if (double.TryParse(uclStrings[i], out var uclValue))
                {
                    spec.UCL = uclValue;
                }
                else
                {
                    throw new System.ArgumentException($"无法解析通道 '{spec.Key}' 的UCL值: '{uclStrings[i]}'");
                }
                double offsetValue = 0;
                if (offsetStrings.Length == keys.Length)
                {
                    if (double.TryParse(offsetStrings[i], out offsetValue))
                    {
                        spec.Offset = offsetValue;
                    }
                    else
                    {
                        throw new System.ArgumentException($"无法解析通道 '{spec.Key}' 的Offset值: '{offsetStrings[i]}'");
                    }
                }
                else
                    spec.Offset = offsetValue;

                step.ExpectedResults.Add(spec);
            }
        }

    }
}
