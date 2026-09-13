using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Extensions.Data;
using ZL.Gear.Extensions.Data.Abstractions;
using ZL.Gear.Extensions.Data.Configuration;
using ZL.Gear.Extensions.Data.Enums;
using ZL.Gear.Extensions.Data.Providers;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Extensions.Data.Tests
{
    [TestFixture]
    public class CsvStorageProviderTests
    {
        private string _testDirectory;
        private CsvConfiguration _config;
        private CsvFileStorageProvider _provider;

        [SetUp]
        public void Setup()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"TestCsv_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDirectory);

            _config = new CsvConfiguration
            {
                Directory = _testDirectory,
                FilePrefix = "TestResults",
                MaxLinesPerFile = 1000
            };

            _provider = new CsvFileStorageProvider(_config);
        }

        [TearDown]
        public void TearDown()
        {
            _provider?.Dispose();
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [Test]
        public void InitializeAsync_创建文件成功()
        {
            Assert.DoesNotThrowAsync(async () => await _provider.InitializeAsync());
            var masterFiles = Directory.GetFiles(_testDirectory, "TestResults_Master_*.csv");
            var detailFiles = Directory.GetFiles(_testDirectory, "TestResults_Detail_*.csv");
            Assert.IsTrue(masterFiles.Length > 0, "Master CSV file should exist");
            Assert.IsTrue(detailFiles.Length > 0, "Detail CSV file should exist");
        }

        [Test]
        public async Task SaveAsync_保存测试结果成功()
        {
            var result = CreateTestResultModel("TEST001", "MODEL-A");

            var saved = await _provider.SaveAsync(result);

            Assert.IsTrue(saved);
            Assert.Greater(result.Id, 0);
            Assert.That(result.Items, Is.All.Matches<TestItemModel>(i => i.TestResultsId == result.Id && i.Id > 0));
        }

        [Test]
        public async Task SaveAsync_两条记录主从Id互不冲突()
        {
            var first = CreateTestResultModel("BAR_A", "MODEL-A");
            var second = CreateTestResultModel("BAR_B", "MODEL-A");

            await _provider.SaveAsync(first);
            await _provider.SaveAsync(second);

            Assert.AreNotEqual(first.Id, second.Id);

            var queriedA = await _provider.QueryByBarcodeAsync("BAR_A");
            var queriedB = await _provider.QueryByBarcodeAsync("BAR_B");

            Assert.AreEqual(1, queriedA.Count);
            Assert.AreEqual(1, queriedB.Count);
            Assert.AreEqual(first.Id, queriedA[0].Id);
            Assert.AreEqual(second.Id, queriedB[0].Id);
            Assert.That(queriedA[0].Items, Is.All.Matches<TestItemModel>(i => i.TestResultsId == first.Id));
            Assert.That(queriedB[0].Items, Is.All.Matches<TestItemModel>(i => i.TestResultsId == second.Id));
        }

        [Test]
        public async Task SaveAsync_并发写入分配唯一主Id()
        {
            const int count = 50;
            var tasks = Enumerable.Range(0, count).Select(i =>
                _provider.SaveAsync(CreateTestResultModel($"CONC_{i:D3}", "MODEL-A")));

            await Task.WhenAll(tasks);

            var (_, total) = await _provider.QueryPagedAsync(1, count + 10);
            Assert.AreEqual(count, total);
        }

        [Test]
        public async Task SaveAsync_逗号字段可正确往返()
        {
            var result = CreateTestResultModel("COMMA_TEST", "MODEL-A");
            result.Items[0].TestValue = "12,5";

            await _provider.SaveAsync(result);

            var queried = await _provider.QueryByBarcodeAsync("COMMA_TEST");
            Assert.AreEqual(1, queried.Count);
            Assert.AreEqual("12,5", queried[0].Items[0].TestValue);
        }

        [Test]
        public async Task UpdateUploadStatusAsync_仅更新指定主Id()
        {
            var first = CreateTestResultModel("UP_A", "MODEL-A");
            var second = CreateTestResultModel("UP_B", "MODEL-A");
            await _provider.SaveAsync(first);
            await _provider.SaveAsync(second);

            var updated = await _provider.UpdateUploadStatusAsync(first.Id, true, "ok");
            Assert.IsTrue(updated);

            var a = await _provider.QueryByBarcodeAsync("UP_A");
            var b = await _provider.QueryByBarcodeAsync("UP_B");
            Assert.IsTrue(a[0].IsTransmitted);
            Assert.IsFalse(b[0].IsTransmitted);
        }

        [Test]
        public async Task SaveAsync_保存空结果返回False()
        {
            var saved = await _provider.SaveAsync(null);

            Assert.IsFalse(saved);
        }

        [Test]
        public async Task SaveBatchAsync_批量保存成功()
        {
            var results = new List<TestResultModel>
            {
                CreateTestResultModel("BAR001", "MODEL-A"),
                CreateTestResultModel("BAR002", "MODEL-A"),
                CreateTestResultModel("BAR003", "MODEL-B")
            };

            var count = await _provider.SaveBatchAsync(results);

            Assert.AreEqual(3, count);
        }

        [Test]
        public async Task QueryByBarcodeAsync_按条码查询成功()
        {
            var barcode = $"BAR_{Guid.NewGuid():N}";
            await _provider.SaveAsync(CreateTestResultModel(barcode, "MODEL-A"));
            await _provider.SaveAsync(CreateTestResultModel("OTHER_BAR", "MODEL-B"));

            var results = await _provider.QueryByBarcodeAsync(barcode);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(barcode, results[0].Barcode);
        }

        [Test]
        public async Task QueryByTimeRangeAsync_按时间范围查询成功()
        {
            var now = DateTime.Now;
            await _provider.SaveAsync(CreateTestResultModel("BAR001", "MODEL-A", now.AddMinutes(-10)));
            await _provider.SaveAsync(CreateTestResultModel("BAR002", "MODEL-A", now.AddMinutes(10)));

            var results = await _provider.QueryByTimeRangeAsync(now.AddMinutes(-5), now.AddMinutes(15));

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public async Task GetPendingUploadAsync_获取待上传数据成功()
        {
            await _provider.SaveAsync(CreateTestResultModel("BAR001", "MODEL-A"));
            await _provider.SaveAsync(CreateTestResultModel("BAR002", "MODEL-A"));

            var results = await _provider.GetPendingUploadAsync(10);

            Assert.GreaterOrEqual(results.Count, 1);
            Assert.IsFalse(results[0].IsTransmitted);
        }

        [Test]
        public async Task UpdateUploadStatusAsync_更新状态成功()
        {
            await _provider.SaveAsync(CreateTestResultModel("BAR001", "MODEL-A"));

            var results = await _provider.GetPendingUploadAsync(1);
            var id = results[0].Id;

            var updated = await _provider.UpdateUploadStatusAsync(id, true, "上传成功");

            Assert.IsTrue(updated);

            var updatedResults = await _provider.QueryByBarcodeAsync("BAR001");
            Assert.IsTrue(updatedResults[0].IsTransmitted);
        }

        [Test]
        public async Task ExportAsync_导出数据成功()
        {
            var exportDir = Path.Combine(_testDirectory, "Export");
            Directory.CreateDirectory(exportDir);

            await _provider.SaveAsync(CreateTestResultModel("BAR001", "MODEL-A", DateTime.Now.AddDays(-1)));
            await _provider.SaveAsync(CreateTestResultModel("BAR002", "MODEL-A", DateTime.Now.AddDays(-1)));

            var exported = await _provider.ExportAsync(exportDir, DateTime.Now.AddDays(-2), DateTime.Now);

            Assert.IsTrue(exported);
            Assert.IsTrue(File.Exists(Path.Combine(exportDir, "Export_Master.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(exportDir, "Export_Detail.csv")));
        }

        private TestResultModel CreateTestResultModel(string barcode, string model, DateTime? testTime = null)
        {
            return new TestResultModel
            {
                Barcode = barcode,
                Model = model,
                StationNo = "STATION-01",
                TestStartTime = testTime ?? DateTime.Now,
                TotalDurationSec = 12.5,
                FinalResult = "PASS",
                IsTransmitted = false,
                Items = new List<TestItemModel>
                {
                    new TestItemModel
                    {
                        StepKey = "STEP_001",
                        TestItem = "Voltage",
                        TestValue = "12.5",
                        Unit = "V",
                        MetricValue = 12.5,
                        TestResult = "PASS",
                        StepStartTime = DateTime.Now
                    },
                    new TestItemModel
                    {
                        StepKey = "STEP_002",
                        TestItem = "Current",
                        TestValue = "0.5",
                        Unit = "A",
                        MetricValue = 0.5,
                        TestResult = "PASS",
                        StepStartTime = DateTime.Now
                    }
                }
            };
        }
    }
}
