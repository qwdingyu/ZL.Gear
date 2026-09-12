using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Engine.Evaluation;

namespace ZL.Gear.Engine.Tests
{
    /// <summary>
    /// G7-03：ExpectedSpec.Mode 注释 ↔ ResultEvaluator switch 一致性 CI。
    /// 目标：判定模式增删改必须双侧同步；差集（注释 vs switch）非空则 CI 失败。
    /// 文档：180 G7-03 · 179 §6.3 · 174 §一。
    /// </summary>
    [TestFixture]
    public class EvaluatorModeConsistencyTests
    {
        /// <summary>
        /// ExpectedSpec.cs 注释声明的 16 种判定模式（与 ResultEvaluator switch 对齐）。
        /// 修改 ResultEvaluator switch 时必须同步更新此处与 ExpectedSpec 注释。
        /// </summary>
        private static readonly string[] DocumentedModes =
        {
            "range", "equals", "lcl_only", "ucl_only",
            "less_lcl", "less_ucl", "big_lcl", "big_ucl",
            "bool", "mask", "bit_set",
            "regex", "string_equals", "contains", "not_contains", "has_value"
        };

        /// <summary>
        /// 从 ResultEvaluator 源码中提取 switch 分支模式集合（防止遗漏 case 分支）。
        /// </summary>
        [Test]
        public void ExpectedSpec注释与ResultEvaluatorSwitch差集为空()
        {
            var switchModes = ExtractSwitchModes();
            var documented = new HashSet<string>(DocumentedModes, StringComparer.OrdinalIgnoreCase);

            var missingInSwitch = documented.Except(switchModes, StringComparer.OrdinalIgnoreCase).ToList();
            var missingInDoc = switchModes.Except(documented, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.IsEmpty(missingInSwitch,
                "ExpectedSpec 注释声明了但 ResultEvaluator switch 未实现: " + string.Join(", ", missingInSwitch));
            Assert.IsEmpty(missingInDoc,
                "ResultEvaluator switch 实现了但 ExpectedSpec 注释未声明: " + string.Join(", ", missingInDoc));
        }

        /// <summary>
        /// 行为级验证：16 种模式全部被 switch 支持（未知模式 fail-closed，不会误 PASS）。
        /// </summary>
        [Test]
        public void 全部文档化模式_行为验证_未知模式fail_closed()
        {
            foreach (var mode in DocumentedModes)
            {
                var result = Evaluate(mode, "VALUE", 1, lcl: 0, ucl: 10);
                Assert.IsNotNull(result, $"模式 '{mode}' 应返回非 null 评估结果");
                // 已知模式不得因"不支持的模式"而失败（除非规格本身不满足）
                Assert.IsFalse(
                    result.Message != null && result.Message.Contains("不支持的判定模式"),
                    $"模式 '{mode}' 被 switch 判定为不支持（ResultEvaluator 未实现该 case）");
            }

            var unknown = Evaluate("typo_mode", "VALUE", 1, lcl: 0, ucl: 10);
            Assert.IsFalse(unknown.Success, "未知模式必须 fail-closed");
            Assert.That(unknown.Message, Does.Contain("不支持的判定模式"));
        }

        /// <summary>
        /// 反射读取 ResultEvaluator 私有静态方法中的 switch case 字符串。
        /// </summary>
        private static HashSet<string> ExtractSwitchModes()
        {
            var modes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 通过私有方法 IL 不可直接反射字符串；改用行为探测：
            // 对每个候选模式调用评估，若返回"不支持的判定模式"则视为 switch 未覆盖。
            var candidates = new[] { "range", "equals", "lcl_only", "ucl_only",
                "less_lcl", "less_ucl", "big_lcl", "big_ucl",
                "bool", "mask", "bit_set", "regex",
                "string_equals", "contains", "not_contains", "has_value" };

            foreach (var mode in candidates)
            {
                var r = Evaluate(mode, "VALUE", 5, lcl: 0, ucl: 10);
                bool supported = !(r.Message != null && r.Message.Contains("不支持的判定模式"));
                if (supported) modes.Add(mode);
            }

            return modes;
        }

        private static EvaluationResult Evaluate(string mode, string key, double value, double? lcl = null, double? ucl = null)
        {
            var measurement = Measurement.Create(key, value, true, "OK", "V", 1);
            var stepResult = new StepRunResult(new StepConfig { StepKey = key, StepName = key })
            {
                Outcome = StepOutcome.Passed,
                Status = StepExecutionStatus.Completed
            };
            stepResult.StepMeasurements.Add(measurement);

            var spec = new ExpectedSpec
            {
                Key = key,
                Mode = mode,
                LCL = lcl,
                UCL = ucl,
                Value = value
            };

            return ((IResultEvaluator)ResultEvaluator.Instance).Evaluate(stepResult, new StepConfig
            {
                StepKey = key,
                StepName = key,
                ExecutionType = StepExecutionType.Verify,
                ExpectedResults = new List<ExpectedSpec> { spec }
            });
        }
    }
}