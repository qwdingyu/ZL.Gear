using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Engine.Evaluation
{
    /// <summary>
    /// 通用结果评估器 (Industrial & ATE Ready)
    /// </summary>
    public static class ResultEvaluator
    {
        /// <summary>
        /// 主评估入口
        /// </summary>
        public static EvaluationResult Evaluate(StepRunResult stepResult, StepConfig config)
        {
            // 1. 第一道防线：执行层面的系统异常 (Crash, Timeout, Cancellation)
            if (stepResult.Status != StepExecutionStatus.Completed || stepResult.Outcome == StepOutcome.Error)
            {
                return EvaluationResult.Fail(stepResult.Message ?? "执行过程中发生未处理异常");
            }

            // 2. 根据步骤意图 (Category) 进行分流策略
            // 如果 config 中没有定义 Category，建议默认视为 Execute 或 Verify (视你的业务偏好而定)
            var category = config.ExecutionType;

            Console.WriteLine($"[ResultEvaluator] ExecutionType={category}, ExpectedResults={(config.ExpectedResults?.Count ?? 0)}");

            switch (category)
            {
                case StepExecutionType.Execute:
                    Console.WriteLine("[ResultEvaluator] 进入 Execute 分支");
                    // 纯动作模式：只要没抛异常（上面已拦截），就视为成功。忽略所有 Spec。
                    return EvaluationResult.Pass("动作执行成功");

                case StepExecutionType.DataCollection:
                    Console.WriteLine("[ResultEvaluator] 进入 DataCollection 分支");
                    // 采集模式：必须有数据
                    if (stepResult.AllMeasurements.Count == 0)
                        return EvaluationResult.Fail("采集失败：未获取到任何测量数据");

                    // 如果采集模式下也配置了 Spec，则作为"有效性过滤器"进行检查
                    // 如果没配 Spec，直接通过
                    if (config.ExpectedResults == null || !config.ExpectedResults.Any())
                        return EvaluationResult.Pass("数据采集成功");
                    break; // 继续往下走 CheckSpecs 逻辑

                case StepExecutionType.Verify:
                default:
                    Console.WriteLine("[ResultEvaluator] 进入 Verify/default 分支");
                    // 校验模式：必须配置规格
                    if (config.ExpectedResults == null || !config.ExpectedResults.Any())
                        return EvaluationResult.Fail("配置错误：校验类步骤(Verify)必须包含期望结果(ExpectedResults)");
                    break; // 继续往下走 CheckSpecs 逻辑
            }

            // 3. 执行核心比对逻辑
            return EvaluateSpecs(stepResult, config);
        }

        /// <summary>
        /// 遍历规格进行逐项检查
        /// </summary>
        private static EvaluationResult EvaluateSpecs(StepRunResult stepResult, StepConfig config)
        {
            var messages = new List<string>();
            bool globalPass = true;

            foreach (var spec in config.ExpectedResults)
            {
                if (string.IsNullOrWhiteSpace(spec.Key)) continue;

                // 查找测量值 (Case-insensitive)
                var measurement = stepResult.AllMeasurements.FirstOrDefault(m => m.Key.Equals(spec.Key, StringComparison.OrdinalIgnoreCase));

                if (measurement == null)
                {
                    globalPass = false;
                    messages.Add($"[FAIL] 缺失数据: 未找到键为 '{spec.Key}' 的测量值");
                    continue;
                }

                // --- 核心判定 ---
                var checkResult = CheckSingleSpec(spec, measurement);

                // 如果有补偿值，创建新的测量对象 (不可变设计)
                if (checkResult.CompensatedValue != null)
                {
                    measurement = Measurement.Create(
                        measurement.Key,
                        checkResult.CompensatedValue,
                        measurement.Success,
                        measurement.Message,
                        measurement.Unit,
                        measurement.SamplesCollected,
                        new Dictionary<string, object>(measurement.Metadata)
                    );
                }

                if (!checkResult.Success) globalPass = false;
                messages.Add(checkResult.Message);
            }

            // 4. 生成汇总消息
            if (globalPass)
            {
                // 成功时，只提取 PASS 的关键信息，保持简洁
                var passInfos = messages.Where(m => m.Contains("[PASS]")).Select(m => ExtractLogContent(m));
                string summary = string.Join("; ", passInfos);
                return EvaluationResult.Pass(string.IsNullOrWhiteSpace(summary) ? "通过" : summary);
            }
            else
            {
                // 失败时，列出所有失败项
                var failInfos = messages.Where(m => m.Contains("[FAIL]")).Select(m => ExtractLogContent(m));
                return EvaluationResult.Fail($"失败: {string.Join("; ", failInfos)}");
            }
        }

        /// <summary>
        /// 单项规格检查分发器
        /// </summary>
        private static (bool Success, string Message, object CompensatedValue) CheckSingleSpec(ExpectedSpec spec, Measurement measurement)
        {
            try
            {
                string mode = spec.Mode?.ToLower() ?? "range";
                object rawValue = measurement.Value;

                // 根据模式分发到不同的处理逻辑
                switch (mode)
                {
                    // --- 1. 数值类 (电检核心) ---
                    case "range":
                    case "equals":
                    case "lcl_only":
                    case "ucl_only":
                        return CheckNumeric(spec, rawValue, mode);

                    // --- 2. 逻辑类 (工控核心) ---
                    case "bool":
                        return CheckBoolean(spec, rawValue);

                    // --- 3. 位操作类 (工控/协议核心) ---
                    case "mask":    // (Val & Mask) == Expected
                    case "bit_set": // (Val >> Index) & 1 == Expected
                        return CheckBitwise(spec, rawValue, mode);

                    // --- 4. 字符串/正则类 (MES/追溯核心) ---
                    case "regex":
                    case "string_equals":
                    case "contains":
                        return CheckString(spec, rawValue, mode);

                    case "has_value":
                        bool hasVal = rawValue != null && !string.IsNullOrWhiteSpace(rawValue.ToString());
                        return (hasVal,
                            hasVal ? $"[PASS] {spec.Key}: 有值" : $"[FAIL] {spec.Key}: 值为空",
                            rawValue);

                    default:
                        return (false, $"[FAIL] {spec.Key}: 不支持的判定模式 '{mode}'", rawValue);
                }
            }
            catch (Exception ex)
            {
                return (false, $"[ERR] {spec.Key}: 判定逻辑异常 - {ex.Message}", measurement.Value);
            }
        }

        #region --- 细分判定逻辑 ---

        private static (bool, string, object) CheckNumeric(ExpectedSpec spec, object rawValue, string mode)
        {
            if (!double.TryParse(rawValue?.ToString(), out double val))
                return (false, $"[FAIL] {spec.Key}: '{rawValue}' 非数字", rawValue);

            // 应用补偿 (Offset)
            double finalVal = val;
            if (spec.Offset.HasValue) finalVal += spec.Offset.Value;

            string valStr = $"{finalVal:F3}{spec.Unit}"; // 格式化显示

            bool passed = false;
            string criteria = "";

            switch (mode)
            {
                case "range":
                    bool minOk = !spec.LCL.HasValue || finalVal >= spec.LCL.Value;
                    bool maxOk = !spec.UCL.HasValue || finalVal <= spec.UCL.Value;
                    passed = minOk && maxOk;
                    criteria = $"[{spec.LCL?.ToString() ?? "-∞"}, {spec.UCL?.ToString() ?? "+∞"}]";
                    break;
                case "equals":
                    double tol = 1E-9; // 浮点容差
                    double target = spec.Value ?? 0;
                    passed = Math.Abs(finalVal - target) < tol;
                    criteria = $"== {target}";
                    break;
                case "lcl_only":
                    passed = !spec.LCL.HasValue || finalVal >= spec.LCL.Value;
                    criteria = $">= {spec.LCL}";
                    break;
                case "ucl_only":
                    passed = !spec.UCL.HasValue || finalVal <= spec.UCL.Value;
                    criteria = $"<= {spec.UCL}";
                    break;
            }

            string resultMsg = passed
                ? $"[PASS] {spec.Key}: {valStr} 在规格 {criteria} 内"
                : $"[FAIL] {spec.Key}: {valStr} 超出规格 {criteria}";

            return (passed, resultMsg, finalVal);
        }

        private static (bool, string, object) CheckBoolean(ExpectedSpec spec, object rawValue)
        {
            // 鲁棒的布尔转换：支持 "1", "true", 1.0, true
            bool actualBool = ToBoolSafe(rawValue);

            // 期望值：优先取 Value (1.0/0.0), 其次取 StringValue ("True")
            bool expectedBool = true; // 默认为 true
            if (spec.Value.HasValue) expectedBool = Math.Abs(spec.Value.Value) > 1E-9;
            else if (!string.IsNullOrEmpty(spec.StringValue)) expectedBool = ToBoolSafe(spec.StringValue);

            bool passed = actualBool == expectedBool;
            string stateStr = actualBool ? "ON/True" : "OFF/False";

            return (passed,
                passed ? $"[PASS] {spec.Key}: 状态 {stateStr} 符合预期" : $"[FAIL] {spec.Key}: 状态 {stateStr} 不符合预期",
                actualBool);
        }

        private static (bool, string, object) CheckBitwise(ExpectedSpec spec, object rawValue, string mode)
        {
            // 转为 long 以支持 32位/64位 整数
            if (!long.TryParse(rawValue?.ToString(), out long intVal))
                return (false, $"[FAIL] {spec.Key}: '{rawValue}' 非整数，无法进行位操作", rawValue);

            bool passed = false;
            string desc = "";

            if (mode == "mask")
            {
                // (Val & Mask) == Expected
                long mask = (long)(spec.LCL ?? 0xFFFF); // 复用 LCL 存 Mask
                long expectedVal = (long)(spec.Value ?? 0);
                long maskedVal = intVal & mask;
                passed = maskedVal == expectedVal;
                desc = $"({intVal:X} & {mask:X}) == {expectedVal:X}";
            }
            else if (mode == "bit_set")
            {
                // (Val >> Index) & 1 == Expected
                int bitIndex = (int)(spec.LCL ?? 0); // 复用 LCL 存 Bit Index
                int expectedBit = (int)(spec.Value ?? 1); // 复用 Value 存期望位状态 (0或1)
                long actualBit = (intVal >> bitIndex) & 1;
                passed = actualBit == expectedBit;
                desc = $"Bit[{bitIndex}] == {expectedBit}";
            }

            return (passed,
                passed ? $"[PASS] {spec.Key}: {desc} 校验通过" : $"[FAIL] {spec.Key}: {desc} 校验失败 (实际值: {intVal:X})",
                intVal);
        }

        private static (bool, string, object) CheckString(ExpectedSpec spec, object rawValue, string mode)
        {
            string strVal = rawValue?.ToString() ?? "";
            string expectedStr = spec.StringValue ?? "";

            bool passed = false;
            string desc = "";

            switch (mode)
            {
                case "string_equals":
                    passed = string.Equals(strVal, expectedStr, StringComparison.OrdinalIgnoreCase);
                    desc = $"== '{expectedStr}'";
                    break;
                case "contains":
                    passed = strVal.IndexOf(expectedStr, StringComparison.OrdinalIgnoreCase) >= 0;
                    desc = $"包含 '{expectedStr}'";
                    break;
                case "regex":
                    // 增加 try-catch 防止正则表达式格式错误导致崩
                    try
                    {
                        passed = Regex.IsMatch(strVal, expectedStr);
                        desc = $"匹配正则 '{expectedStr}'";
                    }
                    catch
                    {
                        return (false, $"[FAIL] {spec.Key}: 无效的正则表达式 '{expectedStr}'", strVal);
                    }
                    break;
            }

            return (passed,
                passed ? $"[PASS] {spec.Key}: '{strVal}' {desc}" : $"[FAIL] {spec.Key}: '{strVal}' 不满足 {desc}",
                strVal);
        }

        #endregion

        #region --- Helpers ---

        private static bool ToBoolSafe(object obj)
        {
            if (obj == null) return false;
            if (obj is bool b) return b;
            string s = obj.ToString();

            // 数字转布尔
            if (double.TryParse(s, out double d)) return Math.Abs(d) > 1E-9;

            // 字符串转布尔
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("on", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
            return false;
        }

        private static string ExtractLogContent(string log)
        {
            // 简单的字符串处理，去掉 [PASS] 前缀，只保留内容
            int idx = log.IndexOf(":");
            return idx >= 0 ? log.Substring(idx + 1).Trim() : log;
        }

        #endregion
    }
}
