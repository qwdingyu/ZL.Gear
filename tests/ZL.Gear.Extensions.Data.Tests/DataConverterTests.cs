using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using ZL.Gear.Extensions.Data.Utilities;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Extensions.Data.Tests
{
    [TestFixture]
    public class DataConverterTests
    {
        [Test]
        public void ToStorageModel_转换成功()
        {
            var runResult = CreateTestRunResult("BARCODE001", "MODEL-A");

            var storageModel = DataConverter.ToStorageModel(runResult, "BARCODE001", "MODEL-A", "STATION-01");

            Assert.AreEqual("BARCODE001", storageModel.Barcode);
            Assert.AreEqual("MODEL-A", storageModel.Model);
            Assert.AreEqual("STATION-01", storageModel.StationNo);
            Assert.AreEqual("PASS", storageModel.FinalResult);
            Assert.AreEqual(runResult.StartTime, storageModel.TestStartTime);
        }

        [Test]
        public void ToStorageModel_OverallSuccess为False时FinalResult为FAIL()
        {
            var runResult = CreateTestRunResult("BARCODE001", "MODEL-A");
            runResult.OverallSuccess = false;

            var storageModel = DataConverter.ToStorageModel(runResult, "BARCODE001", "MODEL-A", "STATION-01");

            Assert.AreEqual("FAIL", storageModel.FinalResult);
        }

        [Test]
        public void ToStorageModel_测量值解析成功()
        {
            var runResult = CreateTestRunResult("BARCODE001", "MODEL-A");

            var storageModel = DataConverter.ToStorageModel(runResult, "BARCODE001", "MODEL-A", "STATION-01");

            var voltageItem = storageModel.Items.FirstOrDefault(i => i.TestItem == "Voltage");
            Assert.IsNotNull(voltageItem);
            Assert.AreEqual("12.5", voltageItem.TestValue);
            Assert.AreEqual(12.5, voltageItem.MetricValue);
            Assert.AreEqual("V", voltageItem.Unit);
        }

        [Test]
        public void ToStorageModel_非数字测量值MetricValue为Null()
        {
            var runResult = CreateTestRunResult("BARCODE001", "MODEL-A");
            runResult.StepResults[0].StepMeasurements.Add(Measurement.Create(
                "Status",
                "ERROR",
                false,
                "状态异常"
            ));

            var storageModel = DataConverter.ToStorageModel(runResult, "BARCODE001", "MODEL-A", "STATION-01");

            var statusItem = storageModel.Items.FirstOrDefault(i => i.TestItem == "Status");
            Assert.IsNotNull(statusItem);
            Assert.AreEqual("ERROR", statusItem.TestValue);
            Assert.IsNull(statusItem.MetricValue);
        }

        [Test]
        public void ToStorageModel_子步骤递归处理成功()
        {
            var runResult = new TestRunResult
            {
                Model = "MODEL-A",
                Barcode = "BARCODE001",
                OverallSuccess = true,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now.AddSeconds(10)
            };

            var parentStep = new StepRunResult(new StepConfig { StepKey = "Parent", StepName = "父步骤" })
            {
                Outcome = StepOutcome.Passed,
                StartTime = DateTime.Now
            };

            var childStep = new StepRunResult(new StepConfig { StepKey = "Child", StepName = "子步骤" })
            {
                Outcome = StepOutcome.Passed,
                StartTime = DateTime.Now,
                StepMeasurements = new List<Measurement>
                {
                    Measurement.Succeeded("ChildValue", 99.9, "", "")
                }
            };
            parentStep.SubStepResults.Add(childStep);
            runResult.StepResults.Add(parentStep);

            var storageModel = DataConverter.ToStorageModel(runResult, "BARCODE001", "MODEL-A", "STATION-01");

            Assert.AreEqual(2, storageModel.Items.Count);
            Assert.IsTrue(storageModel.Items.Any(i => i.StepKey == "Parent"));
            Assert.IsTrue(storageModel.Items.Any(i => i.StepKey == "Child"));
        }

        [Test]
        public void ToMasterEntity_转换成功()
        {
            var model = new Abstractions.TestResultModel
            {
                Id = 1,
                Barcode = "BAR001",
                Model = "MODEL-A",
                StationNo = "STATION-01",
                TestStartTime = DateTime.Now,
                TotalDurationSec = 15.5,
                FinalResult = "PASS",
                IsTransmitted = false
            };

            var entity = model.ToMasterEntity();

            Assert.AreEqual("BAR001", entity.Barcode);
            Assert.AreEqual("MODEL-A", entity.Model);
            Assert.AreEqual(15.5, entity.TotalDurationSec);
        }

        [Test]
        public void ToDetailEntity_转换成功()
        {
            var itemModel = new Abstractions.TestItemModel
            {
                StepKey = "STEP_001",
                TestItem = "Voltage",
                TestValue = "12.5",
                Unit = "V",
                MetricValue = 12.5,
                TestResult = "PASS"
            };

            var entity = itemModel.ToDetailEntity(100);

            Assert.AreEqual(100, entity.TestResultsId);
            Assert.AreEqual("STEP_001", entity.StepKey);
            Assert.AreEqual(12.5, entity.MetricValue);
        }

        private TestRunResult CreateTestRunResult(string barcode, string model)
        {
            var startTime = DateTime.Now;
            var endTime = startTime.AddSeconds(10);

            var step1 = new StepRunResult(new StepConfig { StepKey = "STEP_001", StepName = "电压测量" })
            {
                Outcome = StepOutcome.Passed,
                StartTime = startTime,
                StepMeasurements = new List<Measurement>
                {
                    Measurement.Succeeded("Voltage", 12.5, "", "V"),
                    Measurement.Succeeded("Current", 0.5, "", "A")
                }
            };

            var step2 = new StepRunResult(new StepConfig { StepKey = "STEP_002", StepName = "电阻测量" })
            {
                Outcome = StepOutcome.Passed,
                StartTime = startTime.AddSeconds(5),
                StepMeasurements = new List<Measurement>
                {
                    Measurement.Succeeded("Resistance", 100.0, "", "Ω")
                }
            };

            return new TestRunResult
            {
                Model = model,
                Barcode = barcode,
                OverallSuccess = true,
                StartTime = startTime,
                EndTime = endTime,
                StepResults = new List<StepRunResult> { step1, step2 }
            };
        }
    }
}
