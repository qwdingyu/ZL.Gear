using SqlSugar;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ZL.Gear.Extensions.Data.Abstractions;
using ZL.Gear.Extensions.Data.Configuration;
using ZL.Gear.Extensions.Data.Context;
using ZL.Gear.Extensions.Data.Entities;
using ZL.Gear.Extensions.Data.Utilities;

namespace ZL.Gear.Extensions.Data.Providers
{
    public class SqlDatabaseStorageProvider : IStorageProvider
    {
        private readonly ISqlSugarClient _db;
        private readonly DatabaseConfiguration _config;
        private bool _disposed;

        public Enums.StorageType StorageType => Enums.StorageType.Database;

        public SqlDatabaseStorageProvider(ISqlSugarClient db, DatabaseConfiguration config)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            EnsureSQLiteOnly();
        }

        public SqlDatabaseStorageProvider(string connectionString, Enums.DatabaseType dbType)
        {
            _config = new DatabaseConfiguration
            {
                DbType = dbType,
                ConnectionString = connectionString,
                CommandTimeout = 30
            };
            EnsureSQLiteOnly();
            _db = DbContextFactory.Create(connectionString, dbType);
        }

        public Task InitializeAsync()
        {
            EnsureSQLiteOnly();
            try
            {
                // SQLite 专用 DDL：与 TestResultEntity/ResultItemEntity 字段对齐；非 CodeFirst 以避免 netstandard2.0 映射差异。
                const string sql = @"
                    CREATE TABLE IF NOT EXISTS TestResults (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Barcode TEXT NOT NULL,
                        StationNo TEXT,
                        Model TEXT,
                        TestStartTime DATETIME,
                        TotalDurationSec REAL,
                        FinalResult TEXT,
                        IsTransmitted INTEGER DEFAULT 0,
                        TransmitTime DATETIME,
                        TransmitMessage TEXT
                    );

                    CREATE TABLE IF NOT EXISTS ResultItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TestResultsId INTEGER NOT NULL,
                        StepKey TEXT,
                        TestItem TEXT,
                        TestValue TEXT,
                        Unit TEXT,
                        LCL TEXT,
                        UCL TEXT,
                        MetricValue REAL,
                        TestResult TEXT,
                        StepStartTime DATETIME
                    );";

                _db.Ado.ExecuteCommand(sql);
            }
            catch (Exception ex)
            {
                throw new StorageInitializationException("SQLite 表结构初始化失败。", ex);
            }

            return Task.CompletedTask;
        }

        private void EnsureSQLiteOnly()
        {
            if (_config.DbType != Enums.DatabaseType.SQLite)
            {
                throw new NotSupportedException(
                    "SqlDatabaseStorageProvider 当前仅支持 SQLite。" +
                    "MySQL / SQL Server 将在多数据库方言适配完成后提供；请设置 DatabaseConfiguration.DbType = SQLite。");
            }
        }

        public Task<bool> SaveAsync(TestResultModel result)
        {
            if (result == null) return Task.FromResult(false);

            try
            {
                _db.Ado.BeginTran();

                var masterEntity = result.ToMasterEntity();
                var masterId = _db.Insertable(masterEntity).ExecuteReturnBigIdentity();
                result.Id = masterId;

                if (result.Items.Any())
                {
                    foreach (var item in result.Items)
                    {
                        item.TestResultsId = masterId;
                    }

                    var detailEntities = result.Items.ToEntities();
                    _db.Insertable(detailEntities).IgnoreColumns(x => x.Id).ExecuteCommand();
                }

                _db.Ado.CommitTran();
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _db.Ado.RollbackTran();
                Console.WriteLine($"[SqlDatabaseStorageProvider] 保存失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<int> SaveBatchAsync(IEnumerable<TestResultModel> results)
        {
            if (results == null) return 0;

            int count = 0;
            foreach (var result in results)
            {
                if (await SaveAsync(result).ConfigureAwait(false))
                {
                    count++;
                }
            }
            return count;
        }

        public async Task<IReadOnlyList<TestResultModel>> QueryByBarcodeAsync(string barcode, DateTime? startTime = null, DateTime? endTime = null)
        {
            var results = new List<TestResultModel>();

            try
            {
                var whereClause = "WHERE Barcode = @Barcode";
                var parameters = new Dictionary<string, object> { { "Barcode", barcode } };

                if (startTime.HasValue)
                {
                    whereClause += " AND TestStartTime >= @StartTime";
                    parameters.Add("StartTime", startTime.Value);
                }
                if (endTime.HasValue)
                {
                    whereClause += " AND TestStartTime <= @EndTime";
                    parameters.Add("EndTime", endTime.Value);
                }

                var sql = $@"SELECT * FROM TestResults {whereClause} ORDER BY TestStartTime DESC";
                using (var dt = _db.Ado.GetDataTable(sql, parameters))
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        var entity = MapDataRowToTestResultEntity(row);
                        var model = entity.ToModel();
                        model.Items = await QueryDetailsAsync(model.Id);
                        results.Add(model);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SqlDatabaseStorageProvider] 按条码查询失败: {ex.Message}");
            }

            return results;
        }

        public async Task<IReadOnlyList<TestResultModel>> QueryByTimeRangeAsync(DateTime startTime, DateTime endTime)
        {
            var results = new List<TestResultModel>();

            try
            {
                var sql = @"SELECT * FROM TestResults WHERE TestStartTime >= @StartTime AND TestStartTime <= @EndTime ORDER BY TestStartTime DESC";
                var parameters = new Dictionary<string, object> { { "StartTime", startTime }, { "EndTime", endTime } };
                
                using (var dt = _db.Ado.GetDataTable(sql, parameters))
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        var entity = MapDataRowToTestResultEntity(row);
                        var model = entity.ToModel();
                        model.Items = await QueryDetailsAsync(model.Id);
                        results.Add(model);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SqlDatabaseStorageProvider] 按时间范围查询失败: {ex.Message}");
            }

            return results;
        }

        public async Task<(IReadOnlyList<TestResultModel> Items, int Total)> QueryPagedAsync(
            int page, int pageSize, string barcode = null, string model = null)
        {
            try
            {
                var whereClause = "WHERE 1=1";
                var countSql = "SELECT COUNT(*) FROM TestResults WHERE 1=1";
                var queryParams = new Dictionary<string, object>();

                // SQLite 专用：模糊匹配使用 GLOB（_ 为字面量）；LIKE 会将 _ 当作单字符通配符导致误匹配。
                if (!string.IsNullOrEmpty(barcode))
                {
                    whereClause += " AND Barcode GLOB @Barcode";
                    countSql += " AND Barcode GLOB @Barcode";
                    queryParams.Add("Barcode", $"*{barcode}*");
                }

                if (!string.IsNullOrEmpty(model))
                {
                    whereClause += " AND Model GLOB @Model";
                    countSql += " AND Model GLOB @Model";
                    queryParams.Add("Model", $"*{model}*");
                }

                var total = _db.Ado.SqlQuery<int>(countSql, queryParams).FirstOrDefault();
                var offset = (page - 1) * pageSize;
                var pagedSql =
                    $@"SELECT * FROM TestResults {whereClause} ORDER BY TestStartTime DESC LIMIT @Limit OFFSET @Offset";
                queryParams["Limit"] = pageSize;
                queryParams["Offset"] = offset;

                var results = new List<TestResultModel>();
                using (var dt = _db.Ado.GetDataTable(pagedSql, queryParams))
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        var entity = MapDataRowToTestResultEntity(row);
                        var modelObj = entity.ToModel();
                        modelObj.Items = await QueryDetailsAsync(modelObj.Id).ConfigureAwait(false);
                        results.Add(modelObj);
                    }
                }

                return (results, total);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SqlDatabaseStorageProvider] 分页查询失败: {ex.Message}");
                return (new List<TestResultModel>(), 0);
            }
        }

        public async Task<IReadOnlyList<TestResultModel>> GetPendingUploadAsync(int batchSize = 20)
        {
            var results = new List<TestResultModel>();

            try
            {
                var sql = @"SELECT * FROM TestResults WHERE IsTransmitted = 0 ORDER BY TestStartTime LIMIT @BatchSize";
                var parameters = new Dictionary<string, object> { { "BatchSize", batchSize } };
                
                using (var dt = _db.Ado.GetDataTable(sql, parameters))
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        var entity = MapDataRowToTestResultEntity(row);
                        var model = entity.ToModel();
                        model.Items = await QueryDetailsAsync(model.Id);
                        results.Add(model);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SqlDatabaseStorageProvider] 获取待上传数据失败: {ex.Message}");
            }

            return results;
        }

        public Task<bool> UpdateUploadStatusAsync(long id, bool success, string message)
        {
            try
            {
                var sql = $"UPDATE TestResults SET IsTransmitted=@IsTransmitted, TransmitTime=@TransmitTime, TransmitMessage=@TransmitMessage WHERE Id=@Id";
                var result = _db.Ado.ExecuteCommand(sql, new
                {
                    IsTransmitted = success,
                    TransmitTime = DateTime.Now,
                    TransmitMessage = message,
                    Id = id
                });

                return Task.FromResult(result > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SqlDatabaseStorageProvider] 更新上传状态失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<bool> ExportAsync(string targetPath, DateTime startTime, DateTime endTime)
        {
            try
            {
                var results = await QueryByTimeRangeAsync(startTime, endTime);

                var masterPath = System.IO.Path.Combine(targetPath, "Export_Master.csv");
                var detailPath = System.IO.Path.Combine(targetPath, "Export_Detail.csv");

                var masterSb = new System.Text.StringBuilder();
                masterSb.AppendLine("Id,Barcode,StationNo,Model,TestStartTime,TotalDurationSec,FinalResult,IsTransmitted,TransmitTime,TransmitMessage");

                var detailSb = new System.Text.StringBuilder();
                detailSb.AppendLine("Id,TestResultsId,StepKey,TestItem,TestValue,Unit,LCL,UCL,MetricValue,TestResult,StepStartTime");

                foreach (var result in results)
                {
                    masterSb.AppendLine($"{result.Id},{result.Barcode},{result.StationNo},{result.Model}," +
                        $"{result.TestStartTime:yyyy-MM-dd HH:mm:ss},{result.TotalDurationSec},{result.FinalResult}," +
                        $"{result.IsTransmitted},{result.TransmitTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""},{result.TransmitMessage}");

                    foreach (var item in result.Items)
                    {
                        detailSb.AppendLine($"{item.Id},{item.TestResultsId},{item.StepKey},{item.TestItem}," +
                            $"{item.TestValue},{item.Unit},{item.LCL},{item.UCL}," +
                            $"{item.MetricValue?.ToString() ?? ""},{item.TestResult},{item.StepStartTime:yyyy-MM-dd HH:mm:ss}");
                    }
                }

                System.IO.File.WriteAllText(masterPath, masterSb.ToString());
                System.IO.File.WriteAllText(detailPath, detailSb.ToString());

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SqlDatabaseStorageProvider] 导出失败: {ex.Message}");
                return false;
            }
        }

        private TestResultEntity MapDataRowToTestResultEntity(DataRow row)
        {
            return new TestResultEntity
            {
                Id = Convert.ToInt64(row["Id"]),
                Barcode = row["Barcode"]?.ToString() ?? string.Empty,
                StationNo = row["StationNo"]?.ToString() ?? string.Empty,
                Model = row["Model"]?.ToString() ?? string.Empty,
                TestStartTime = Convert.ToDateTime(row["TestStartTime"]),
                TotalDurationSec = Convert.ToDouble(row["TotalDurationSec"]),
                FinalResult = row["FinalResult"]?.ToString() ?? string.Empty,
                IsTransmitted = Convert.ToBoolean(row["IsTransmitted"]),
                TransmitTime = row["TransmitTime"] == DBNull.Value ? null : Convert.ToDateTime(row["TransmitTime"]),
                TransmitMessage = row["TransmitMessage"]?.ToString()
            };
        }

        private ResultItemEntity MapDataRowToResultItemEntity(DataRow row)
        {
            return new ResultItemEntity
            {
                Id = Convert.ToInt64(row["Id"]),
                TestResultsId = Convert.ToInt64(row["TestResultsId"]),
                StepKey = row["StepKey"]?.ToString() ?? string.Empty,
                TestItem = row["TestItem"]?.ToString() ?? string.Empty,
                TestValue = row["TestValue"]?.ToString(),
                Unit = row["Unit"]?.ToString(),
                LCL = row["LCL"]?.ToString(),
                UCL = row["UCL"]?.ToString(),
                MetricValue = row["MetricValue"] == DBNull.Value ? null : Convert.ToDouble(row["MetricValue"]),
                TestResult = row["TestResult"]?.ToString() ?? string.Empty,
                StepStartTime = Convert.ToDateTime(row["StepStartTime"])
            };
        }

        private async Task<List<TestItemModel>> QueryDetailsAsync(long masterId)
        {
            var sql = @"SELECT * FROM ResultItems WHERE TestResultsId = @MasterId";
            var parameters = new Dictionary<string, object> { { "MasterId", masterId } };
            
            using (var dt = _db.Ado.GetDataTable(sql, parameters))
            {
                var items = new List<TestItemModel>();
                foreach (DataRow row in dt.Rows)
                {
                    var entity = MapDataRowToResultItemEntity(row);
                    items.Add(entity.ToModel());
                }
                return items;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _db.Dispose();
                _disposed = true;
            }
        }
    }

    internal static class ItemModelExtensions
    {
        public static List<ResultItemEntity> ToEntities(this IEnumerable<TestItemModel> models)
        {
            return models.Select(m => new ResultItemEntity
            {
                TestResultsId = m.TestResultsId,
                StepKey = m.StepKey,
                TestItem = m.TestItem,
                TestValue = m.TestValue,
                Unit = m.Unit,
                LCL = m.LCL,
                UCL = m.UCL,
                MetricValue = m.MetricValue,
                TestResult = m.TestResult,
                StepStartTime = m.StepStartTime
            }).ToList();
        }
    }
}
