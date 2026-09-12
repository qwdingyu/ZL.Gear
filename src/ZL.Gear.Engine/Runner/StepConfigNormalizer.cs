using System;
using System.Collections.Generic;
using System.Linq;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// StepConfig 归一化工具（Parameters legacy 桥接 + 公开仓扩展）。
    /// 职责：TargetDict、DurationMs、EvaluateResult 桥接、LCL/UCL/Offset → ExpectedResults、递归 SubSteps。
    /// </summary>
    public static class StepConfigNormalizer
    {
        // 兼容电阻仪“无穷大”哨兵值（legacy ResultEvaluator / Normalizer 共用语义）
        private const double InfinitySentinel = 9.9999E+20;
        private const double InfinityEpsilon = 1E+15;

        /// <summary>
        /// 归一化 StepConfig。
        /// </summary>
        public static void Normalize(StepConfig step, IDictionary<string, object> deviceRoles)
        {
            if (step == null) return;

            MergeTargetDict(step, deviceRoles);
            NormalizeDurationMs(step);
            BridgeEvaluateResult(step);

            if (step.ExpectedResults == null || !step.ExpectedResults.Any())
            {
                TryBuildExpectedResultsFromLegacyParameters(step);
            }

            if (step.SubSteps != null && step.SubSteps.Any())
            {
                foreach (var subStep in step.SubSteps)
                {
                    Normalize(subStep, deviceRoles);
                }
            }
        }

        private static void MergeTargetDict(StepConfig step, IDictionary<string, object> deviceRoles)
        {
            var merged = new Dictionary<string, object>();
            if (deviceRoles != null && step.AdditionalTargets != null)
            {
                foreach (var key in step.AdditionalTargets)
                {
                    if (string.IsNullOrWhiteSpace(key) || merged.ContainsKey(key)) continue;
                    if (deviceRoles.TryGetValue(key, out var val))
                        merged[key] = val;
                }
            }

            if (step.TargetDict == null)
            {
                step.TargetDict = merged;
            }
            else
            {
                foreach (var kv in merged)
                {
                    if (!step.TargetDict.ContainsKey(kv.Key))
                        step.TargetDict[kv.Key] = kv.Value;
                }
            }

            if (!string.IsNullOrEmpty(step.Target) && step.TargetDict != null && !step.TargetDict.ContainsKey("Main"))
            {
                step.TargetDict["Main"] = step.Target;
                if (!step.TargetDict.ContainsKey(step.Target))
                    step.TargetDict[step.Target] = step.Target;
            }
        }

        private static void NormalizeDurationMs(StepConfig step)
        {
            var parameters = step.Parameters ?? (step.Parameters = new Dictionary<string, object>());
            int timeoutMs = step.TimeoutMs;
            int safeDuration = timeoutMs > 500 ? timeoutMs - 500 : 0;

            if (parameters.ContainsKey("DurationMs"))
            {
                try
                {
                    var dur = parameters.Get<int>("DurationMs");
                    if (dur > timeoutMs) parameters["DurationMs"] = safeDuration;
                }
                catch
                {
                    parameters["DurationMs"] = safeDuration;
                }
            }
            else
            {
                parameters["DurationMs"] = safeDuration;
            }
        }

        private static void BridgeEvaluateResult(StepConfig step)
        {
            var parameters = step.Parameters;
            if (parameters == null) return;
            if (!step.EvaluateResult.HasValue
                && parameters.TryGetValue("EvaluateResult", out var evalObj)
                && evalObj is bool evalBool)
            {
                step.EvaluateResult = evalBool;
            }
        }

        private static void TryBuildExpectedResultsFromLegacyParameters(StepConfig step)
        {
            var parameters = step.Parameters ?? (step.Parameters = new Dictionary<string, object>());

            var keyStr = ResolveSpecKey(step);
            if (string.IsNullOrWhiteSpace(keyStr)) return;

            bool hasLcl = parameters.ContainsKey("LCL");
            bool hasUcl = parameters.ContainsKey("UCL");
            bool hasOffset = parameters.ContainsKey("Offset");
            if (!hasLcl && !hasUcl && !hasOffset) return;

            string lclStr = GetParamAsString(parameters, "LCL");
            string uclStr = GetParamAsString(parameters, "UCL");
            string offsetStr = GetParamAsString(parameters, "Offset");
            string unit = GetParamAsString(parameters, "Unit");

            if (string.IsNullOrWhiteSpace(lclStr) && string.IsNullOrWhiteSpace(uclStr) && string.IsNullOrWhiteSpace(offsetStr))
                return;

            step.ExpectedResults = new List<ExpectedSpec>();

            try
            {
                NormalizeMultiValue(step, keyStr, lclStr, uclStr, offsetStr, unit);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"在规范化步骤 '{step.StepName}' 的期望结果时发生错误。", ex);
            }
        }

        /// <summary>legacy：MeasurementKey 参数 → StepKey → StepName（空白时继续回退）。</summary>
        private static string ResolveSpecKey(StepConfig step)
        {
            if (step.Parameters != null
                && step.Parameters.TryGetValue("MeasurementKey", out var mk)
                && mk != null)
            {
                var explicitKey = mk.ToString();
                if (!string.IsNullOrWhiteSpace(explicitKey))
                    return explicitKey.Trim();
            }

            if (!string.IsNullOrWhiteSpace(step.StepKey))
                return step.StepKey.Trim();

            if (!string.IsNullOrWhiteSpace(step.StepName))
                return step.StepName.Trim();

            return null;
        }

        private static string GetParamAsString(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var v) || v == null) return null;
            return v.ToString();
        }

        private static void NormalizeMultiValue(StepConfig step, string keyStr, string lclStr, string uclStr, string offsetStr, string unit)
        {
            var keys = SplitList(keyStr);
            var lcls = SplitListAllowEmpty(lclStr);
            var ucls = SplitListAllowEmpty(uclStr);
            var offsets = SplitListAllowEmpty(offsetStr);
            if (offsets.Length == 0) offsets = Enumerable.Repeat("0", keys.Length).ToArray();

            lcls = BroadcastIfSingle(lcls, keys.Length);
            ucls = BroadcastIfSingle(ucls, keys.Length);
            offsets = BroadcastIfSingle(offsets, keys.Length);

            if (lcls.Length != keys.Length || ucls.Length != keys.Length || offsets.Length != keys.Length)
            {
                throw new ArgumentException(
                    $"参数数量不匹配。'channel'({keys.Length})、'LCL'({lcls.Length})、'UCL'({ucls.Length})、'Offset'({offsets.Length}) 的项目数量必须相同，或 LCL/UCL/Offset 允许单值广播。");
            }

            step.ExpectedResults = new List<ExpectedSpec>(keys.Length);

            for (int i = 0; i < keys.Length; i++)
            {
                var spec = new ExpectedSpec
                {
                    Key = keys[i].Trim(),
                    Mode = "range",
                    Unit = unit
                };

                if (!TryParseLimit(lcls[i], isUpper: false, out var lclValue))
                    throw new ArgumentException($"无法解析通道 '{spec.Key}' 的 LCL 值: '{lcls[i]}'");
                spec.LCL = lclValue;

                if (!TryParseLimit(ucls[i], isUpper: true, out var uclValue))
                    throw new ArgumentException($"无法解析通道 '{spec.Key}' 的 UCL 值: '{ucls[i]}'");
                spec.UCL = uclValue;

                if (!TryParseOffset(offsets[i], out var off))
                    throw new ArgumentException($"无法解析通道 '{spec.Key}' 的 Offset 值: '{offsets[i]}'");
                if (Math.Abs(off) > double.Epsilon) spec.Offset = off;

                step.ExpectedResults.Add(spec);
            }
        }

        private static string[] SplitList(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
            return s.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        private static string[] SplitListAllowEmpty(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
            return s.Split(new[] { '|' }, StringSplitOptions.None)
                .Select(x => x?.Trim())
                .ToArray();
        }

        private static string[] BroadcastIfSingle(string[] arr, int targetLen)
        {
            if (arr == null || arr.Length == 0)
                return Enumerable.Repeat("", targetLen).ToArray();

            if (arr.Length == 1 && targetLen > 1)
                return Enumerable.Repeat(arr[0] ?? "", targetLen).ToArray();

            return arr;
        }

        private static bool TryParseOffset(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return true;
            return double.TryParse(s, out value);
        }

        private static bool TryParseLimit(string raw, bool isUpper, out double? value)
        {
            value = null;

            if (string.IsNullOrWhiteSpace(raw))
                return true;

            var s = raw.Trim();

            if (IsInfinityText(s, isUpper))
                return true;

            if (!double.TryParse(s, out var v))
                return false;

            if (isUpper && IsPositiveInfinityLike(v))
                return true;
            if (!isUpper && IsNegativeInfinityLike(v))
                return true;

            value = v;
            return true;
        }

        private static bool IsInfinityText(string s, bool isUpper)
        {
            var t = s.Replace(" ", "").ToLowerInvariant();

            if (isUpper)
            {
                return t == "∞" || t == "+∞" || t == "inf" || t == "+inf" || t == "infinity" || t == "+infinity";
            }

            return t == "-∞" || t == "-inf" || t == "-infinity";
        }

        private static bool IsPositiveInfinityLike(double v)
        {
            if (double.IsPositiveInfinity(v)) return true;
            if (Math.Abs(v - InfinitySentinel) <= InfinityEpsilon) return true;
            if (v >= InfinitySentinel) return true;
            return false;
        }

        private static bool IsNegativeInfinityLike(double v)
        {
            if (double.IsNegativeInfinity(v)) return true;
            if (Math.Abs(v + InfinitySentinel) <= InfinityEpsilon) return true;
            if (v <= -InfinitySentinel) return true;
            return false;
        }
    }
}
