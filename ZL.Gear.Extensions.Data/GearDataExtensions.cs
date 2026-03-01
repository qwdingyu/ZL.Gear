using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.Gear.Extensions.Data.Abstractions;
using ZL.Gear.Extensions.Data.Configuration;
using ZL.Gear.Extensions.Data.Context;
using ZL.Gear.Extensions.Data.Utilities;
using CoreRunner = ZL.Gear.Core.Runner;

namespace ZL.Gear.Extensions.Data
{
    public static class GearDataExtensions
    {
        private static IStorageProvider _storageProvider;
        private static readonly object _lockObj = new object();

        public static IStorageProvider Current => _storageProvider ?? throw new InvalidOperationException("请先调用 Initialize 方法初始化存储提供者");

        public static void Initialize(StorageConfiguration config)
        {
            lock (_lockObj)
            {
                _storageProvider?.Dispose();
                _storageProvider = StorageProviderFactory.Create(config);
                _storageProvider.InitializeAsync().GetAwaiter().GetResult();
            }
        }

        public static void InitializeSqlite(string filePath)
        {
            var config = new StorageConfiguration
            {
                StorageType = Enums.StorageType.Database,
                Database = new DatabaseConfiguration
                {
                    DbType = Enums.DatabaseType.SQLite,
                    ConnectionString = $"DataSource={filePath}"
                }
            };
            Initialize(config);
        }

        public static void InitializeMySql(string server, int port, string database, string user, string password)
        {
            var config = new StorageConfiguration
            {
                StorageType = Enums.StorageType.Database,
                Database = new DatabaseConfiguration
                {
                    DbType = Enums.DatabaseType.MySql,
                    ConnectionString = $"Server={server};Port={port};Database={database};User={user};Password={password};Charset=utf8mb4;"
                }
            };
            Initialize(config);
        }

        public static void InitializeSqlServer(string server, string database, string user, string password)
        {
            var config = new StorageConfiguration
            {
                StorageType = Enums.StorageType.Database,
                Database = new DatabaseConfiguration
                {
                    DbType = Enums.DatabaseType.SqlServer,
                    ConnectionString = $"Server={server};Database={database};User Id={user};Password={password};"
                }
            };
            Initialize(config);
        }

        public static void InitializeCsv(string directory, string filePrefix = "TestResults")
        {
            var config = new StorageConfiguration
            {
                StorageType = Enums.StorageType.CsvFile,
                Csv = new CsvConfiguration
                {
                    Directory = directory,
                    FilePrefix = filePrefix
                }
            };
            Initialize(config);
        }

        public static async Task<bool> SaveTestRunAsync(this CoreRunner.TestRunResult runResult, string barcode, string model, string stationNo)
        {
            var storageModel = DataConverter.ToStorageModel(runResult, barcode, model, stationNo);
            return await Current.SaveAsync(storageModel);
        }

        public static async Task<int> SaveTestRunsAsync(this IEnumerable<CoreRunner.TestRunResult> runResults, string barcode, string model, string stationNo)
        {
            var storageModels = runResults.Select(r => DataConverter.ToStorageModel(r, barcode, model, stationNo));
            return await Current.SaveBatchAsync(storageModels);
        }

        public static async Task<IReadOnlyList<TestResultModel>> QueryByBarcodeAsync(string barcode, DateTime? startTime = null, DateTime? endTime = null)
        {
            return await Current.QueryByBarcodeAsync(barcode, startTime, endTime);
        }

        public static async Task<IReadOnlyList<TestResultModel>> QueryByTimeRangeAsync(DateTime startTime, DateTime endTime)
        {
            return await Current.QueryByTimeRangeAsync(startTime, endTime);
        }

        public static async Task<(IReadOnlyList<TestResultModel> Items, int Total)> QueryPagedAsync(int page, int pageSize, string barcode = null, string model = null)
        {
            return await Current.QueryPagedAsync(page, pageSize, barcode, model);
        }

        public static async Task<IReadOnlyList<TestResultModel>> GetPendingUploadAsync(int batchSize = 20)
        {
            return await Current.GetPendingUploadAsync(batchSize);
        }

        public static async Task<bool> UpdateUploadStatusAsync(long id, bool success, string message)
        {
            return await Current.UpdateUploadStatusAsync(id, success, message);
        }

        public static async Task<bool> ExportAsync(string targetPath, DateTime startTime, DateTime endTime)
        {
            return await Current.ExportAsync(targetPath, startTime, endTime);
        }

        public static void Dispose()
        {
            lock (_lockObj)
            {
                _storageProvider?.Dispose();
                _storageProvider = null;
            }
        }
    }
}
