using System;
using System.Collections.Generic;
using System.Linq;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// StepConfig 归一化工具。
    /// 职责：兼容旧格式参数、桥接 EvaluateResult、递归归一化子步骤。
    /// </summary>
    public static class StepConfigNormalizer
    {
        /// <summary>
        /// 归一化 StepConfig：兼容旧格式参数、桥接 EvaluateResult、递归处理子步骤。
        /// </summary>
        /// <param name="step">要进行归一化的步骤配置。</param>
        /// <param name="deviceRoles">设备角色映射表。</param>
        /// <remarks>
        /// 主要处理：
        /// 1. 将 AdditionalTargets 映射到 TargetDict；
        /// 2. 保证 DurationMs 不超过总超时；
        /// 3. 从 Parameters 回填 EvaluateResult；
        /// 4. 旧格式 LCL/UCL/Offset 转 ExpectedResults；
        /// 5. 递归归一化 SubSteps。
        /// </remarks>
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
            if (step.TargetDict == null)
            {
                step.TargetDict = TargetDict;
            }
            else
            {
                foreach (var kv in TargetDict)
                {
                    if (!step.TargetDict.ContainsKey(kv.Key))
                    {
                        step.TargetDict.Add(kv.Key, kv.Value);
                    }
                }
            }

            // 确保主 Target 也在 TargetDict 中（若 BindProfile 未提前处理，则在此补全）
            if (!string.IsNullOrEmpty(step.Target) && !step.TargetDict.ContainsKey("Main"))
            {
                step.TargetDict["Main"] = step.Target;
                if (!step.TargetDict.ContainsKey(step.Target))
                {
                    step.TargetDict[step.Target] = step.Target;
                }
            }
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

            // 桥接 EvaluateResult：若步骤未显式设置，且参数中明确提供，则回填
            if (!step.EvaluateResult.HasValue && parameters.TryGetValue("EvaluateResult", out object evalObj) && evalObj is bool evalBool)
            {
                step.EvaluateResult = evalBool;
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
