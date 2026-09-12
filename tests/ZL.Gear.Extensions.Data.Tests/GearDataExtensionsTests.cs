using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ZL.Gear.Extensions.Data;
using ZL.Gear.Extensions.Data.Abstractions;
using ZL.Gear.Extensions.Data.Configuration;
using ZL.Gear.Extensions.Data.Enums;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Extensions.Data.Tests
{
    [TestFixture]
    public class GearDataExtensionsTests
    {
        private string _dbFilePath;

        [SetUp]
        public void Setup()
        {
            _dbFilePath = Path.Combine(Path.GetTempPath(), $"TestExtensions_{Guid.NewGuid():N}.db");
            GearDataExtensions.InitializeSqlite(_dbFilePath);
        }

        [TearDown]
        public void TearDown()
        {
            GearDataExtensions.Dispose();
            if (File.Exists(_dbFilePath))
            {
                File.Delete(_dbFilePath);
            }
        }

        [Test]
        public async Task SaveTestRunAsync_扩展方法保存成功()
        {
            var runResult = CreateTestRunResult("BARCODE001", "MODEL-A");

            var saved = await runResult.SaveTestRunAsync("BARCODE001", "MODEL-A", "STATION-01");

            Assert.IsTrue(saved);
        }

        [Test]
        public async Task SaveTestRunsAsync_批量保存成功()
        {
            var results = new List<TestRunResult>
            {
                CreateTestRunResult("BAR001", "MODEL-A"),
                CreateTestRunResult("BAR002", "MODEL-A"),
                CreateTestRunResult("BAR003", "MODEL-B")
            };

            var count = await results.SaveTestRunsAsync("BAR001", "MODEL-A", "STATION-01");

            Assert.AreEqual(3, count);
        }

        [Test]
        public async Task QueryByBarcodeAsync_查询成功()
        {
            var barcode = $"BAR_{Guid.NewGuid():N}";
            var runResult = CreateTestRunResult(barcode, "MODEL-A");
            await runResult.SaveTestRunAsync(barcode, "MODEL-A", "STATION-01");

            var results = await GearDataExtensions.QueryByBarcodeAsync(barcode);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(barcode, results[0].Barcode);
        }

        [Test]
        public async Task QueryByTimeRangeAsync_按时间范围查询成功()
        {
            var now = DateTime.Now;
            var runResult = CreateTestRunResult("BAR001", "MODEL-A", now.AddMinutes(-5));
            await runResult.SaveTestRunAsync("BAR001", "MODEL-A", "STATION-01");

            var results = await GearDataExtensions.QueryByTimeRangeAsync(now.AddMinutes(-10), now);

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public async Task QueryPagedAsync_分页查询成功()
        {
            for (int i = 0; i < 10; i++)
            {
                var runResult = CreateTestRunResult($"BAR{i:000}", "MODEL-A");
                await runResult.SaveTestRunAsync($"BAR{i:000}", "MODEL-A", "STATION-01");
            }

            var (items, total) = await GearDataExtensions.QueryPagedAsync(1, 5);

            Assert.AreEqual(5, items.Count);
            Assert.AreEqual(10, total);
        }

        [Test]
        public async Task GetPendingUploadAsync_获取待上传数据成功()
        {
            var runResult = CreateTestRunResult("BAR001", "MODEL-A");
            await runResult.SaveTestRunAsync("BAR001", "MODEL-A", "STATION-01");

            var results = await GearDataExtensions.GetPendingUploadAsync(10);

            Assert.GreaterOrEqual(1, results.Count);
        }

        [Test]
        public async Task UpdateUploadStatusAsync_更新状态成功()
        {
            var runResult = CreateTestRunResult("BAR001", "MODEL-A");
            await runResult.SaveTestRunAsync("BAR001", "MODEL-A", "STATION-01");

            var results = await GearDataExtensions.GetPendingUploadAsync(1);
            if (results.Count > 0)
            {
                var id = results[0].Id;
                var updated = await GearDataExtensions.UpdateUploadStatusAsync(id, true, "测试成功");
                Assert.IsTrue(updated);
            }
        }

        [Test]
        public async Task ExportAsync_导出成功()
        {
            var runResult = CreateTestRunResult("BAR001", "MODEL-A", DateTime.Now.AddDays(-1));
            await runResult.SaveTestRunAsync("BAR001", "MODEL-A", "STATION-01");

            var exportDir = Path.Combine(Path.GetTempPath(), $"ExportTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(exportDir);

            try
            {
                var exported = await GearDataExtensions.ExportAsync(exportDir, DateTime.Now.AddDays(-2), DateTime.Now);

                Assert.IsTrue(exported);
                Assert.IsTrue(File.Exists(Path.Combine(exportDir, "Export_Master.csv")));
            }
            finally
            {
                Directory.Delete(exportDir, true);
            }
        }

        private TestRunResult CreateTestRunResult(string barcode, string model, DateTime? testTime = null)
        {
            var startTime = testTime ?? DateTime.Now;
            var endTime = startTime.AddSeconds(10);

            var step1 = new StepRunResult(new StepConfig { StepKey = "STEP_001", StepName = "电压测量" })
            {
                Outcome = StepOutcome.Passed,
                StartTime = startTime,
                StepMeasurements = new List<Measurement>
                {
                    Measurement.Succeeded("Voltage", 12.5, "", "V")
                }
            };

            return new TestRunResult
            {
                Model = model,
                Barcode = barcode,
                OverallSuccess = true,
                StartTime = startTime,
                EndTime = endTime,
                StepResults = new List<StepRunResult> { step1 }
            };
        }
    }
}
