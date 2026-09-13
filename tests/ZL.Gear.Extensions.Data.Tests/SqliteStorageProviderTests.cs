using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using ZL.Gear.Extensions.Data;
using ZL.Gear.Extensions.Data.Abstractions;
using ZL.Gear.Extensions.Data.Configuration;
using ZL.Gear.Extensions.Data.Context;
using ZL.Gear.Extensions.Data.Enums;
using ZL.Gear.Extensions.Data.Providers;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Extensions.Data.Tests
{
    [TestFixture]
    public class SqliteStorageProviderTests
    {
        private string _dbFilePath;
        private SqlDatabaseStorageProvider _provider;

        [SetUp]
        public void Setup()
        {
            _dbFilePath = Path.Combine(Path.GetTempPath(), $"TestSQLite_{Guid.NewGuid():N}.db");
            _provider = new SqlDatabaseStorageProvider(_dbFilePath, DatabaseType.SQLite);
            _provider.InitializeAsync().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _provider?.Dispose();
            if (File.Exists(_dbFilePath))
            {
                File.Delete(_dbFilePath);
            }
        }

        [Test]
        public void InitializeAsync_初始化表结构成功()
        {
            Assert.DoesNotThrowAsync(async () => await _provider.InitializeAsync());
        }

        [Test]
        public void Constructor_MySql_抛出NotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() =>
                new SqlDatabaseStorageProvider("Server=localhost;", DatabaseType.MySql));
        }

        [Test]
        public async Task SaveAsync_保存测试结果成功()
        {
            var result = CreateTestResultModel("TEST001", "MODEL-A");

            var saved = await _provider.SaveAsync(result);

            Assert.IsTrue(saved);
            Assert.Greater(result.Id, 0);
        }

        [Test]
        public async Task SaveAsync_保存空结果返回False()
        {
            var saved = await _provider.SaveAsync(null);

            Assert.IsFalse(saved);
        }

        [Test]
        public async Task SaveAsync_主从表关联正确()
        {
            var result = CreateTestResultModel("TEST001", "MODEL-A");
            result.Items.Add(new TestItemModel
            {
                StepKey = "STEP_001",
                TestItem = "Voltage",
                TestValue = "12.5",
                Unit = "V",
                MetricValue = 12.5,
                TestResult = "PASS",
                StepStartTime = DateTime.Now
            });

            await _provider.SaveAsync(result);

            var queried = await _provider.QueryByBarcodeAsync("TEST001");
            Assert.AreEqual(1, queried.Count);
            Assert.AreEqual(3, queried[0].Items.Count);
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
        public async Task QueryByBarcodeAsync_按条码和时间范围查询()
        {
            var barcode = $"BAR_{Guid.NewGuid():N}";
            var now = DateTime.Now;
            await _provider.SaveAsync(CreateTestResultModel(barcode, "MODEL-A", now.AddMinutes(-10)));
            await _provider.SaveAsync(CreateTestResultModel(barcode, "MODEL-A", now.AddMinutes(10)));

            var results = await _provider.QueryByBarcodeAsync(barcode, now.AddMinutes(-5), now.AddMinutes(15));

            Assert.AreEqual(1, results.Count);
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
        public async Task QueryPagedAsync_分页查询成功()
        {
            for (int i = 0; i < 15; i++)
            {
                await _provider.SaveAsync(CreateTestResultModel($"BAR{i:000}", "MODEL-A"));
            }

            var (items, total) = await _provider.QueryPagedAsync(1, 10);

            Assert.AreEqual(10, items.Count);
            Assert.AreEqual(15, total);
        }

        [Test]
        public async Task QueryPagedAsync_按条件筛选()
        {
            for (int i = 0; i < 5; i++)
            {
                await _provider.SaveAsync(CreateTestResultModel($"A_BAR{i}", "MODEL-A"));
                await _provider.SaveAsync(CreateTestResultModel($"B_BAR{i}", "MODEL-B"));
            }

            var (items, total) = await _provider.QueryPagedAsync(1, 20, barcode: "A_");

            Assert.AreEqual(5, items.Count);
            Assert.AreEqual(5, total);
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
            Assert.AreEqual("上传成功", updatedResults[0].TransmitMessage);
        }

        [Test]
        public async Task ExportAsync_导出数据成功()
        {
            var exportDir = Path.Combine(Path.GetTempPath(), $"Export_{Guid.NewGuid():N}");
            Directory.CreateDirectory(exportDir);

            await _provider.SaveAsync(CreateTestResultModel("BAR001", "MODEL-A", DateTime.Now.AddDays(-1)));
            await _provider.SaveAsync(CreateTestResultModel("BAR002", "MODEL-A", DateTime.Now.AddDays(-1)));

            var exported = await _provider.ExportAsync(exportDir, DateTime.Now.AddDays(-2), DateTime.Now);

            Assert.IsTrue(exported);
            Assert.IsTrue(File.Exists(Path.Combine(exportDir, "Export_Master.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(exportDir, "Export_Detail.csv")));

            Directory.Delete(exportDir, true);
        }

        [Test]
        public async Task DataIntegrity_主从表事务一致性()
        {
            var result = CreateTestResultModel("TRANSACTION_TEST", "MODEL-A");

            var saved = await _provider.SaveAsync(result);

            var queried = await _provider.QueryByBarcodeAsync("TRANSACTION_TEST");
            Assert.AreEqual(1, queried.Count);
            Assert.AreEqual(result.Items.Count, queried[0].Items.Count);
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
