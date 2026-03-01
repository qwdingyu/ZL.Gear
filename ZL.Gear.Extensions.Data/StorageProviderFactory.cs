using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Extensions.Data.Abstractions;
using ZL.Gear.Extensions.Data.Configuration;
using ZL.Gear.Extensions.Data.Context;
using ZL.Gear.Extensions.Data.Providers;

namespace ZL.Gear.Extensions.Data
{
    public static class StorageProviderFactory
    {
        public static IStorageProvider Create(StorageConfiguration config)
        {
            return config.StorageType switch
            {
                Enums.StorageType.Database => CreateDatabaseProvider(config.Database),
                Enums.StorageType.CsvFile => new CsvFileStorageProvider(config.Csv),
                Enums.StorageType.Mixed => new MixedStorageProvider(config),
                _ => throw new NotSupportedException($"不支持的存储类型: {config.StorageType}")
            };
        }

        public static IStorageProvider CreateDatabase(string connectionString, Enums.DatabaseType dbType = Enums.DatabaseType.SQLite)
        {
            var config = new DatabaseConfiguration
            {
                DbType = dbType,
                ConnectionString = connectionString
            };
            return CreateDatabaseProvider(config);
        }

        public static IStorageProvider CreateSqlite(string filePath)
        {
            return CreateDatabase($"DataSource={filePath}", Enums.DatabaseType.SQLite);
        }

        public static IStorageProvider CreateMySql(string server, int port, string database, string user, string password)
        {
            var connectionString = $"Server={server};Port={port};Database={database};User={user};Password={password};Charset=utf8mb4;";
            return CreateDatabase(connectionString, Enums.DatabaseType.MySql);
        }

        public static IStorageProvider CreateSqlServer(string server, string database, string user, string password)
        {
            var connectionString = $"Server={server};Database={database};User Id={user};Password={password};";
            return CreateDatabase(connectionString, Enums.DatabaseType.SqlServer);
        }

        public static IStorageProvider CreateCsv(string directory, string filePrefix = "TestResults")
        {
            var config = new CsvConfiguration
            {
                Directory = directory,
                FilePrefix = filePrefix
            };
            return new CsvFileStorageProvider(config);
        }

        private static IStorageProvider CreateDatabaseProvider(DatabaseConfiguration config)
        {
            var db = DbContextFactory.Create(config);
            return new SqlDatabaseStorageProvider(db, config);
        }
    }

    internal class MixedStorageProvider : IStorageProvider
    {
        private readonly IStorageProvider _dbProvider;
        private readonly IStorageProvider _csvProvider;

        public Enums.StorageType StorageType => Enums.StorageType.Mixed;

        public MixedStorageProvider(StorageConfiguration config)
        {
            _dbProvider = CreateDatabaseProvider(config.Database);
            _csvProvider = new CsvFileStorageProvider(config.Csv);
        }

        public Task InitializeAsync()
        {
            return Task.WhenAll(
                _dbProvider.InitializeAsync(),
                _csvProvider.InitializeAsync()
            );
        }

        public async Task<bool> SaveAsync(TestResultModel result)
        {
            var dbResult = await _dbProvider.SaveAsync(result);
            var csvResult = await _csvProvider.SaveAsync(result);
            return dbResult && csvResult;
        }

        public async Task<int> SaveBatchAsync(IEnumerable<TestResultModel> results)
        {
            var dbCount = await _dbProvider.SaveBatchAsync(results);
            var csvCount = await _csvProvider.SaveBatchAsync(results);
            return Math.Min(dbCount, csvCount);
        }

        public Task<IReadOnlyList<TestResultModel>> QueryByBarcodeAsync(string barcode, DateTime? startTime = null, DateTime? endTime = null)
        {
            return _dbProvider.QueryByBarcodeAsync(barcode, startTime, endTime);
        }

        public Task<IReadOnlyList<TestResultModel>> QueryByTimeRangeAsync(DateTime startTime, DateTime endTime)
        {
            return _dbProvider.QueryByTimeRangeAsync(startTime, endTime);
        }

        public Task<(IReadOnlyList<TestResultModel> Items, int Total)> QueryPagedAsync(int page, int pageSize, string barcode = null, string model = null)
        {
            return _dbProvider.QueryPagedAsync(page, pageSize, barcode, model);
        }

        public Task<IReadOnlyList<TestResultModel>> GetPendingUploadAsync(int batchSize = 20)
        {
            return _dbProvider.GetPendingUploadAsync(batchSize);
        }

        public Task<bool> UpdateUploadStatusAsync(long id, bool success, string message)
        {
            return _dbProvider.UpdateUploadStatusAsync(id, success, message);
        }

        public Task<bool> ExportAsync(string targetPath, DateTime startTime, DateTime endTime)
        {
            return _csvProvider.ExportAsync(targetPath, startTime, endTime);
        }

        public void Dispose()
        {
            _dbProvider.Dispose();
            _csvProvider.Dispose();
        }

        private static IStorageProvider CreateDatabaseProvider(DatabaseConfiguration config)
        {
            var db = DbContextFactory.Create(config);
            return new SqlDatabaseStorageProvider(db, config);
        }
    }
}
