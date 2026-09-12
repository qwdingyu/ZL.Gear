using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZL.Gear.Core.Models;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine.Tests
{
    /// <summary>
    /// legacy StepConfigNormalizer 行为验收（盐城 265 步 LCL/UCL 隐式 range 主路径）。
    /// legacy 行为回归：单边 LCL/UCL、∞ 哨兵 9.9999E+20、StepKey 回退。
    /// 文档：ZL.Gear.Docs/174 §三 · 175 §十五 P0
    /// </summary>
    [TestFixture]
    public class StepConfigNormalizerLegacyPortTests
    {
        private static readonly Dictionary<string, object> EmptyRoles = new Dictionary<string, object>();

        #region 当前公开仓已支持（回归 · 参与默认 CI）

        [Test]
        public void Normalize_双键LCL_UCL_生成range规格()
        {
            var step = LegacyStyleStep(
                stepName: "座垫向上电流",
                lcl: "1.5",
                ucl: "7.0",
                unit: "A");

            StepConfigNormalizer.Normalize(step, EmptyRoles);

            Assert.That(step.ExpectedResults, Is.Not.Null.And.Count.EqualTo(1));
            var spec = step.ExpectedResults[0];
            Assert.AreEqual("座垫向上电流", spec.Key);
            Assert.AreEqual("range", spec.Mode);
            Assert.AreEqual(1.5, spec.LCL!.Value, 1e-9);
            Assert.AreEqual(7.0, spec.UCL!.Value, 1e-9);
            Assert.AreEqual("A", spec.Unit);
        }

        [Test]
        public void Normalize_已有ExpectedResults_不覆盖()
        {
            var step = LegacyStyleStep("锁扣", lcl: "0", ucl: "999999");
            step.ExpectedResults = new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "锁扣", Mode = "big_ucl", UCL = 999999 }
            };

            StepConfigNormalizer.Normalize(step, EmptyRoles);

            Assert.AreEqual(1, step.ExpectedResults.Count);
            Assert.AreEqual("big_ucl", step.ExpectedResults[0].Mode);
        }

        #endregion

        #region legacy 专有行为（已 port · 默认 CI）

        [Test]
        [Category("LegacyNormalizerPort")]
        public void LegacyNormalize_仅LCL无UCL_仍生成range()
        {
            var step = new StepConfig
            {
                StepName = "仅下限",
                StepKey = "仅下限",
                TimeoutMs = 5000,
                Parameters = new Dictionary<string, object>
                {
                    ["LCL"] = "0.5",
                    ["Unit"] = "A"
                }
            };

            StepConfigNormalizer.Normalize(step, EmptyRoles);

            Assert.That(step.ExpectedResults, Is.Not.Null.And.Not.Empty,
                "legacy TryBuildExpectedResultsFromLegacyParameters 允许仅 LCL");
            Assert.AreEqual("range", step.ExpectedResults![0].Mode);
            Assert.AreEqual(0.5, step.ExpectedResults[0].LCL!.Value, 1e-9);
        }

        [Test]
        [Category("LegacyNormalizerPort")]
        public void LegacyNormalize_仅UCL无LCL_仍生成range()
        {
            var step = new StepConfig
            {
                StepName = "仅上限",
                StepKey = "仅上限",
                TimeoutMs = 5000,
                Parameters = new Dictionary<string, object>
                {
                    ["UCL"] = "10",
                    ["Unit"] = "V"
                }
            };

            StepConfigNormalizer.Normalize(step, EmptyRoles);

            Assert.That(step.ExpectedResults, Is.Not.Null.And.Not.Empty);
            Assert.AreEqual(10.0, step.ExpectedResults![0].UCL!.Value, 1e-9);
        }

        [Test]
        [Category("LegacyNormalizerPort")]
        public void LegacyNormalize_UCL为无穷哨兵_归一化为null()
        {
            var step = LegacyStyleStep("电阻", lcl: "0", ucl: "9.9999E+20", unit: "Ω");

            StepConfigNormalizer.Normalize(step, EmptyRoles);

            Assert.That(step.ExpectedResults, Is.Not.Null.And.Not.Empty);
            Assert.IsNull(step.ExpectedResults![0].UCL,
                "legacy TryParseLimit 将 9.9999E+20 视为 +∞ → null（不限制上限）");
        }

        [Test]
        [Category("LegacyNormalizerPort")]
        public void LegacyNormalize_无MeasurementKey参数_StepKey作规格键()
        {
            var step = new StepConfig
            {
                StepName = "",
                StepKey = "SBR电阻测试1",
                TimeoutMs = 5000,
                Parameters = new Dictionary<string, object>
                {
                    ["LCL"] = "100",
                    ["UCL"] = "300",
                    ["Unit"] = "Ω"
                }
            };

            StepConfigNormalizer.Normalize(step, EmptyRoles);

            Assert.That(step.ExpectedResults, Is.Not.Null.And.Not.Empty);
            Assert.AreEqual("SBR电阻测试1", step.ExpectedResults![0].Key,
                "legacy: MeasurementKey ?? StepKey ?? StepName");
        }

        [Test]
        [Category("LegacyNormalizerPort")]
        public void LegacyNormalize_UCL为无穷文本_归一化为null()
        {
            var step = LegacyStyleStep("电阻", lcl: "0", ucl: "+∞", unit: "Ω");

            StepConfigNormalizer.Normalize(step, EmptyRoles);

            Assert.That(step.ExpectedResults, Is.Not.Null.And.Not.Empty);
            Assert.IsNull(step.ExpectedResults![0].UCL);
        }

        #endregion

        private static StepConfig LegacyStyleStep(string stepName, string lcl, string ucl, string unit = "")
        {
            return new StepConfig
            {
                StepName = stepName,
                StepKey = stepName,
                TimeoutMs = 10000,
                Parameters = new Dictionary<string, object>
                {
                    ["LCL"] = lcl,
                    ["UCL"] = ucl,
                    ["Unit"] = unit
                }
            };
        }
    }
}
