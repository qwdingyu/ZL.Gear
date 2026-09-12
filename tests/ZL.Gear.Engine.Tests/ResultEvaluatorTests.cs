using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Engine.Evaluation;

namespace ZL.Gear.Engine.Tests
{
    [TestFixture]
    public class ResultEvaluatorTests
    {
        [SetUp]
        public void Setup()
        {
        }

        #region Execute 类型

        [Test]
        public void Evaluate_Execute类型_无ExpectedResults_通过()
        {
            var stepResult = CreateStepResult(StepOutcome.Passed, new List<Measurement>());
            var config = CreateConfig(StepExecutionType.Execute, null);

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void Evaluate_Execute类型_有ExpectedResults_忽略规格并通过()
        {
            var stepResult = CreateStepResult(StepOutcome.Passed, new List<Measurement>());
            var config = CreateConfig(StepExecutionType.Execute, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Voltage", Mode = "range", LCL = 10, UCL = 20 }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
            Assert.That(result.Message, Does.Contain("动作执行成功"));
            Assert.That(result.Message, Does.Contain("忽略 ExpectedResults"));
        }

        #endregion

        #region DataCollection 类型

        [Test]
        public void Evaluate_DataCollection类型_无数据_失败()
        {
            var stepResult = CreateStepResult(StepOutcome.Passed, new List<Measurement>());
            var config = CreateConfig(StepExecutionType.DataCollection, null);

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("未获取到任何测量数据"));
        }

        [Test]
        public void Evaluate_DataCollection类型_有数据_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Temp", 25.0, "正常", "°C")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.DataCollection, null);

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
            Assert.That(result.Message, Does.Contain("数据采集成功"));
        }

        [Test]
        public void Evaluate_Verify类型_无ExpectedResults_失败()
        {
            var stepResult = CreateStepResult(StepOutcome.Passed, new List<Measurement>());
            var config = CreateConfig(StepExecutionType.Verify, null);

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("必须包含期望结果"));
        }

        [Test]
        public void Evaluate_Verify类型_有ExpectedResults_正常判定()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Voltage", 12.0, "正常", "V")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Voltage", Mode = "range", LCL = 10, UCL = 15, Unit = "V" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
            Assert.That(result.Message, Does.Contain("在规格"));
        }

        #endregion

        #region 判定模式 - 数值类

        [Test]
        public void EvaluateSpecs_range_在范围内_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Voltage", 12.0, "", "V")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Voltage", Mode = "range", LCL = 10, UCL = 15, Unit = "V" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
            Assert.That(result.Message, Does.Contain("在规格"));
        }

        [Test]
        public void EvaluateSpecs_range_超出范围_失败()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Voltage", 20.0, "", "V")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Voltage", Mode = "range", LCL = 10, UCL = 15, Unit = "V" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("超出规格"));
        }

        [Test]
        public void EvaluateSpecs_equals_相等_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Resistance", 100.0, "", "Ω")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Resistance", Mode = "equals", Value = 100.0, Unit = "Ω" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void EvaluateSpecs_lcl_only_大于等于_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Current", 0.5, "", "A")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Current", Mode = "lcl_only", LCL = 0.3, Unit = "A" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void EvaluateSpecs_ucl_only_小于等于_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Current", 0.5, "", "A")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Current", Mode = "ucl_only", UCL = 1.0, Unit = "A" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        #endregion

        #region 判定模式 - 逻辑类

        [Test]
        public void EvaluateSpecs_bool_为True_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Relay", true, "", "")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Relay", Mode = "bool", Value = 1.0 }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void EvaluateSpecs_bool_为False_失败()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Relay", false, "", "")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Relay", Mode = "bool", Value = 1.0 }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsFalse(result.Success);
        }

        #endregion

        #region 判定模式 - 位操作类

        [Test]
        public void EvaluateSpecs_mask_位匹配_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Status", 0x03, "", "")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Status", Mode = "mask", LCL = 0x0F, Value = 0x03 }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void EvaluateSpecs_bit_set_指定位为1_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Status", 0x01, "", "")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Status", Mode = "bit_set", LCL = 0, Value = 1 } // Bit[0] == 1
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        #endregion

        #region 判定模式 - 字符串类

        [Test]
        public void EvaluateSpecs_string_equals_匹配_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Serial", "ABC123", "", "")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Serial", Mode = "string_equals", StringValue = "ABC123" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void EvaluateSpecs_contains_包含子串_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Message", "PASS: OK", "", "")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Message", Mode = "contains", StringValue = "PASS" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void EvaluateSpecs_regex_匹配正则_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Barcode", "SN-2024-001", "", "")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Barcode", Mode = "regex", StringValue = @"^SN-\d{4}-\d{3}$" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        #endregion

        #region 判定模式 - has_value

        [Test]
        public void EvaluateSpecs_has_value_有值_通过()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Value", 12.5, "", "V")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Value", Mode = "has_value" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void EvaluateSpecs_has_value_空值_失败()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Value", null, "", "")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Value", Mode = "has_value" }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("值为空"));
        }

        #endregion

        #region 单位不匹配

        [Test]
        public void EvaluateSpecs_单位不匹配_失败()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Voltage", 12.0, "", "V")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Voltage", Mode = "range", LCL = 10, UCL = 15, Unit = "mV" } // 单位不匹配
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("规格单位"));
        }

        #endregion

        #region 缺失数据

        [Test]
        public void EvaluateSpecs_缺失测量值_失败()
        {
            var measurements = new List<Measurement>
            {
                Measurement.Succeeded("Current", 0.5, "", "A")
            };
            var stepResult = CreateStepResult(StepOutcome.Passed, measurements);
            var config = CreateConfig(StepExecutionType.Verify, new List<ExpectedSpec>
            {
                new ExpectedSpec { Key = "Voltage", Mode = "range", LCL = 10, UCL = 15 }
            });

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("未找到键为 'Voltage' 的测量值"));
        }

        #endregion

        #region 执行异常

        [Test]
        public void Evaluate_执行异常_失败()
        {
            var stepResult = CreateStepResult(StepOutcome.Error, new List<Measurement>());
            stepResult.Message = "设备通信超时";
            var config = CreateConfig(StepExecutionType.Verify, null);

            var result = ResultEvaluator.Evaluate(stepResult, config);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("设备通信超时"));
        }

        #endregion

        #region 辅助方法

        private StepRunResult CreateStepResult(StepOutcome outcome, List<Measurement> measurements)
        {
            return new StepRunResult(new StepConfig { StepKey = "TEST", StepName = "Test" })
            {
                Outcome = outcome,
                Status = StepExecutionStatus.Completed,
                StepMeasurements = measurements,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now.AddSeconds(1)
            };
        }

        private StepConfig CreateConfig(StepExecutionType executionType, List<ExpectedSpec> expectedResults)
        {
            return new StepConfig
            {
                StepKey = "CONFIG-001",
                Command = "Test",
                ExecutionType = executionType,
                ExpectedResults = expectedResults
            };
        }

        #endregion
    }
}
