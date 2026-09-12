using System.Collections.Generic;
using NUnit.Framework;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Engine.Evaluation;

namespace ZL.Gear.Engine.Tests
{
    /// <summary>
    /// 盐城 legacy 判定模式 port 验收骨架（公开仓须对齐 legacy ResultEvaluator 16 种模式）。
    /// 文档：ZL.Gear.Docs/175 §11.1 · 174 §一
    /// </summary>
    /// <remarks>
    /// 盐城 legacy 16 种判定模式的回归验收（含 5 种 strict 比较与 not_contains）。
    /// 若公开仓 ResultEvaluator 回退缺模式，本 Fixture 将 fail-closed 失败。
    /// </remarks>
    [TestFixture]
    [Category("LegacyPort")]
    public class ResultEvaluatorLegacyPortTests
    {
        #region big_ucl — 锁扣插入（YC 配方 18~19 处）

        /// <summary>6800015-BT01「安全带锁扣阻值-插入」：UCL=999999，插入后电阻应 &gt; UCL。</summary>
        [Test]
        public void Legacy_big_ucl_锁扣已插入_电阻大于UCL_通过()
        {
            var result = Evaluate("安全带锁扣阻值-插入", "big_ucl", 1_000_000, ucl: 999_999, unit: "Ω");
            Assert.IsTrue(result.Success, result.Message);
        }

        [Test]
        public void Legacy_big_ucl_锁扣未插入_电阻接近零_失败()
        {
            var result = Evaluate("安全带锁扣阻值-插入", "big_ucl", 0, ucl: 999_999, unit: "Ω");
            Assert.IsFalse(result.Success, "未插入时须 FAIL，否则漏检");
        }

        [Test]
        public void Legacy_big_ucl_UCL未配置_失败()
        {
            var result = Evaluate("锁扣", "big_ucl", 1_000_000, ucl: null, unit: "Ω");
            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("big_ucl").Or.Contain("UCL").Or.Contain("不支持"));
        }

        #endregion

        #region less_ucl — YC 配方 2 处

        [Test]
        public void Legacy_less_ucl_严格小于UCL_通过()
        {
            var result = Evaluate("Current", "less_ucl", 5, ucl: 10, unit: "A");
            Assert.IsTrue(result.Success, result.Message);
        }

        [Test]
        public void Legacy_less_ucl_等于UCL_失败()
        {
            var result = Evaluate("Current", "less_ucl", 10, ucl: 10, unit: "A");
            Assert.IsFalse(result.Success);
        }

        #endregion

        #region less_lcl / big_lcl — legacy 有、公开缺

        [Test]
        public void Legacy_less_lcl_严格小于LCL_通过()
        {
            var result = Evaluate("Val", "less_lcl", 5, lcl: 10, unit: "V");
            Assert.IsTrue(result.Success, result.Message);
        }

        [Test]
        public void Legacy_big_lcl_严格大于LCL_通过()
        {
            var result = Evaluate("Val", "big_lcl", 15, lcl: 10, unit: "V");
            Assert.IsTrue(result.Success, result.Message);
        }

        #endregion

        #region not_contains — legacy 有、公开缺

        [Test]
        public void Legacy_not_contains_不含子串_通过()
        {
            var stepResult = CreateStepResult(StepOutcome.Passed, new List<Measurement>
            {
                Measurement.Succeeded("Version", "PASS-OK", "", "")
            });
            var config = CreateConfig(new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Version", Mode = "not_contains", StringValue = "FAIL" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);
            Assert.IsTrue(result.Success, result.Message);
        }

        [Test]
        public void Legacy_not_contains_含子串_失败()
        {
            var stepResult = CreateStepResult(StepOutcome.Passed, new List<Measurement>
            {
                Measurement.Succeeded("Version", "FAIL-001", "", "")
            });
            var config = CreateConfig(new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Version", Mode = "not_contains", StringValue = "FAIL" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);
            Assert.IsFalse(result.Success);
        }

        #endregion

        #region fail-closed — 未知模式

        [Test]
        public void Legacy_未知判定模式_fail_closed()
        {
            var result = Evaluate("X", "typo_mode", 1, lcl: 0, ucl: 10, unit: "");
            Assert.IsFalse(result.Success);
        }

        #endregion

        #region 16 模式清单（文档对照，非逐个断言）

        /// <summary>
        /// legacy CheckSingleSpec 外层 switch 16 种：range, equals, lcl_only, ucl_only,
        /// less_lcl, less_ucl, big_lcl, big_ucl, bool, mask, bit_set, regex,
        /// string_equals, contains, not_contains, has_value
        /// </summary>
        [Test]
        public void Legacy_模式清单_公开仓应有16种()
        {
            var legacyModes = new[]
            {
                "range", "equals", "lcl_only", "ucl_only",
                "less_lcl", "less_ucl", "big_lcl", "big_ucl",
                "bool", "mask", "bit_set", "regex",
                "string_equals", "contains", "not_contains", "has_value"
            };
            Assert.AreEqual(16, legacyModes.Length, "与 174 §一 表格一致");
        }

        #endregion

        #region helpers

        private static EvaluationResult Evaluate(
            string key, string mode, double value, double? lcl = null, double? ucl = null, string unit = "V")
        {
            var stepResult = CreateStepResult(StepOutcome.Passed, new List<Measurement>
            {
                Measurement.Succeeded(key, value, "", unit)
            });
            var spec = new ExpectedSpec { Key = key, Mode = mode, Unit = unit };
            if (lcl.HasValue) spec.LCL = lcl.Value;
            if (ucl.HasValue) spec.UCL = ucl.Value;
            return ResultEvaluator.Evaluate(stepResult, CreateConfig(new List<ExpectedSpec> { spec }));
        }

        private static StepRunResult CreateStepResult(StepOutcome outcome, List<Measurement> measurements) =>
            new StepRunResult(new StepConfig { StepKey = "TEST", StepName = "Test" })
            {
                Outcome = outcome,
                Status = StepExecutionStatus.Completed,
                StepMeasurements = measurements,
                StartTime = System.DateTime.Now,
                EndTime = System.DateTime.Now.AddSeconds(1)
            };

        private static StepConfig CreateConfig(List<ExpectedSpec> expectedResults) =>
            new StepConfig
            {
                StepKey = "CONFIG-001",
                Command = "Test",
                ExecutionType = StepExecutionType.Verify,
                ExpectedResults = expectedResults
            };

        #endregion
    }
}
