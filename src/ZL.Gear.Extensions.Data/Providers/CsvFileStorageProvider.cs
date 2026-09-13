using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZL.Gear.Extensions.Data.Abstractions;
using ZL.Gear.Extensions.Data.Configuration;
using ZL.Gear.Extensions.Data.Utilities;

namespace ZL.Gear.Extensions.Data.Providers
{
    public class CsvFileStorageProvider : IStorageProvider
    {
        private readonly CsvConfiguration _config;
        private readonly string _masterFilePath;
        private readonly string _detailFilePath;
        private readonly object _lockObj = new object();
        private long _nextMasterId = 1;
        private long _nextDetailId = 1;
        private bool _disposed;

        public Enums.StorageType StorageType => Enums.StorageType.CsvFile;

        public CsvFileStorageProvider(CsvConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Directory.CreateDirectory(_config.Directory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _masterFilePath = Path.Combine(_config.Directory, $"{_config.FilePrefix}_Master_{timestamp}.csv");
            _detailFilePath = Path.Combine(_config.Directory, $"{_config.FilePrefix}_Detail_{timestamp}.csv");

            lock (_lockObj)
            {
                InitializeFiles();
                SeedIdCountersFromFiles();
            }
        }

        public Task InitializeAsync()
        {
            lock (_lockObj)
            {
                InitializeFiles();
                SeedIdCountersFromFiles();
            }

            return Task.CompletedTask;
        }

        private void InitializeFiles()
        {
            if (!File.Exists(_masterFilePath))
            {
                var masterHeader = "Id,Barcode,StationNo,Model,TestStartTime,TotalDurationSec,FinalResult,IsTransmitted,TransmitTime,TransmitMessage";
                File.WriteAllText(_masterFilePath, masterHeader + Environment.NewLine, Encoding.UTF8);
            }

            if (!File.Exists(_detailFilePath))
            {
                var detailHeader = "Id,TestResultsId,StepKey,TestItem,TestValue,Unit,LCL,UCL,MetricValue,TestResult,StepStartTime";
                File.WriteAllText(_detailFilePath, detailHeader + Environment.NewLine, Encoding.UTF8);
            }
        }

        public Task<bool> SaveAsync(TestResultModel result)
        {
            if (result == null)
            {
                return Task.FromResult(false);
            }

            lock (_lockObj)
            {
                try
                {
                    return Task.FromResult(SaveCore(result));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CsvFileStorageProvider] 保存失败: {ex.Message}");
                    return Task.FromResult(false);
                }
            }
        }

        private bool SaveCore(TestResultModel result)
        {
            CheckAndRotateFile(_masterFilePath);
            CheckAndRotateFile(_detailFilePath);

            var masterId = result.Id > 0 ? result.Id : _nextMasterId++;
            if (result.Id <= 0)
            {
                result.Id = masterId;
            }
            else if (masterId >= _nextMasterId)
            {
                _nextMasterId = masterId + 1;
            }

            AppendLine(_masterFilePath, FormatMasterLine(result));

            foreach (var item in result.Items)
            {
                item.TestResultsId = masterId;
                if (item.Id <= 0)
                {
                    item.Id = _nextDetailId++;
                }
                else if (item.Id >= _nextDetailId)
                {
                    _nextDetailId = item.Id + 1;
                }

                AppendLine(_detailFilePath, FormatDetailLine(item));
            }

            return true;
        }

        public async Task<int> SaveBatchAsync(IEnumerable<TestResultModel> results)
        {
            if (results == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var result in results)
            {
                if (await SaveAsync(result).ConfigureAwait(false))
                {
                    count++;
                }
            }

            return count;
        }

        public Task<IReadOnlyList<TestResultModel>> QueryByBarcodeAsync(string barcode, DateTime? startTime = null, DateTime? endTime = null)
        {
            lock (_lockObj)
            {
                try
                {
                    var results = QueryMasterRecords(record =>
                    {
                        if (record.Barcode != barcode)
                        {
                            return false;
                        }

                        if (startTime.HasValue && record.TestStartTime < startTime.Value)
                        {
                            return false;
                        }

                        if (endTime.HasValue && record.TestStartTime > endTime.Value)
                        {
                            return false;
                        }

                        return true;
                    });

                    return Task.FromResult<IReadOnlyList<TestResultModel>>(results);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CsvFileStorageProvider] 按条码查询失败: {ex.Message}");
                    return Task.FromResult<IReadOnlyList<TestResultModel>>(new List<TestResultModel>());
                }
            }
        }

        public Task<IReadOnlyList<TestResultModel>> QueryByTimeRangeAsync(DateTime startTime, DateTime endTime)
        {
            lock (_lockObj)
            {
                try
                {
                    var results = QueryMasterRecords(record =>
                        record.TestStartTime >= startTime && record.TestStartTime <= endTime);
                    return Task.FromResult<IReadOnlyList<TestResultModel>>(results);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CsvFileStorageProvider] 按时间范围查询失败: {ex.Message}");
                    return Task.FromResult<IReadOnlyList<TestResultModel>>(new List<TestResultModel>());
                }
            }
        }

        public Task<(IReadOnlyList<TestResultModel> Items, int Total)> QueryPagedAsync(int page, int pageSize, string barcode = null, string model = null)
        {
            lock (_lockObj)
            {
                var allResults = QueryMasterRecords(record =>
                {
                    if (!string.IsNullOrEmpty(barcode) && record.Barcode != barcode)
                    {
                        return false;
                    }

                    if (!string.IsNullOrEmpty(model) && record.Model != model)
                    {
                        return false;
                    }

                    return true;
                });

                var total = allResults.Count;
                var pagedItems = allResults
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Task.FromResult<(IReadOnlyList<TestResultModel> Items, int Total)>((pagedItems, total));
            }
        }

        public Task<IReadOnlyList<TestResultModel>> GetPendingUploadAsync(int batchSize = 20)
        {
            lock (_lockObj)
            {
                try
                {
                    var results = QueryMasterRecords(record => !record.IsTransmitted)
                        .Take(batchSize)
                        .ToList();
                    return Task.FromResult<IReadOnlyList<TestResultModel>>(results);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CsvFileStorageProvider] 获取待上传数据失败: {ex.Message}");
                    return Task.FromResult<IReadOnlyList<TestResultModel>>(new List<TestResultModel>());
                }
            }
        }

        public Task<bool> UpdateUploadStatusAsync(long id, bool success, string message)
        {
            lock (_lockObj)
            {
                try
                {
                    var lines = ReadAllLines(_masterFilePath).ToList();
                    if (lines.Count <= 1)
                    {
                        return Task.FromResult(false);
                    }

                    var updated = false;
                    var sb = new StringBuilder();
                    sb.AppendLine(lines[0]);

                    for (var i = 1; i < lines.Count; i++)
                    {
                        var line = lines[i];
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var parts = CsvFormat.ParseLine(line);
                        if (parts.Length < 10)
                        {
                            sb.AppendLine(line);
                            continue;
                        }

                        if (long.TryParse(parts[0], out var recordId) && recordId == id)
                        {
                            var model = ParseMasterLine(parts);
                            model.IsTransmitted = success;
                            model.TransmitTime = DateTime.Now;
                            model.TransmitMessage = message;
                            line = FormatMasterLine(model);
                            updated = true;
                        }

                        sb.AppendLine(line);
                    }

                    if (!updated)
                    {
                        return Task.FromResult(false);
                    }

                    File.WriteAllText(_masterFilePath, sb.ToString(), Encoding.UTF8);
                    return Task.FromResult(true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CsvFileStorageProvider] 更新上传状态失败: {ex.Message}");
                    return Task.FromResult(false);
                }
            }
        }

        public async Task<bool> ExportAsync(string targetPath, DateTime startTime, DateTime endTime)
        {
            try
            {
                var results = await QueryByTimeRangeAsync(startTime, endTime).ConfigureAwait(false);

                Directory.CreateDirectory(targetPath);
                var masterPath = Path.Combine(targetPath, "Export_Master.csv");
                var detailPath = Path.Combine(targetPath, "Export_Detail.csv");

                var masterSb = new StringBuilder();
                masterSb.AppendLine("Id,Barcode,StationNo,Model,TestStartTime,TotalDurationSec,FinalResult,IsTransmitted,TransmitTime,TransmitMessage");

                var detailSb = new StringBuilder();
                detailSb.AppendLine("Id,TestResultsId,StepKey,TestItem,TestValue,Unit,LCL,UCL,MetricValue,TestResult,StepStartTime");

                foreach (var result in results)
                {
                    masterSb.AppendLine(FormatMasterLine(result));

                    foreach (var item in result.Items)
                    {
                        detailSb.AppendLine(FormatDetailLine(item));
                    }
                }

                File.WriteAllText(masterPath, masterSb.ToString(), Encoding.UTF8);
                File.WriteAllText(detailPath, detailSb.ToString(), Encoding.UTF8);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CsvFileStorageProvider] 导出失败: {ex.Message}");
                return false;
            }
        }

        private List<TestResultModel> QueryMasterRecords(Func<TestResultModel, bool> predicate)
        {
            var results = new List<TestResultModel>();
            var allLines = ReadAllLines(_masterFilePath);

            foreach (var line in allLines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = CsvFormat.ParseLine(line);
                if (parts.Length < 10)
                {
                    continue;
                }

                var result = ParseMasterLine(parts);
                if (!predicate(result))
                {
                    continue;
                }

                result.Items = QueryDetailsByMasterId(result.Id);
                results.Add(result);
            }

            return results;
        }

        private List<TestItemModel> QueryDetailsByMasterId(long masterId)
        {
            var items = new List<TestItemModel>();
            var allLines = ReadAllLines(_detailFilePath);

            foreach (var line in allLines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = CsvFormat.ParseLine(line);
                if (parts.Length < 11)
                {
                    continue;
                }

                if (long.TryParse(parts[1], out var pid) && pid == masterId)
                {
                    items.Add(ParseDetailLine(parts));
                }
            }

            return items;
        }

        private void SeedIdCountersFromFiles()
        {
            _nextMasterId = Math.Max(1, ReadMaxIdColumn(_masterFilePath, 0) + 1);
            _nextDetailId = Math.Max(1, ReadMaxIdColumn(_detailFilePath, 0) + 1);
        }

        private static long ReadMaxIdColumn(string path, int columnIndex)
        {
            if (!File.Exists(path))
            {
                return 0;
            }

            long max = 0;
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Id,", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = CsvFormat.ParseLine(line);
                if (parts.Length > columnIndex && long.TryParse(parts[columnIndex], out var id))
                {
                    if (id > max)
                    {
                        max = id;
                    }
                }
            }

            return max;
        }

        private string FormatMasterLine(TestResultModel model)
        {
            return CsvFormat.JoinFields(new[]
            {
                model.Id.ToString(),
                CsvFormat.EscapeField(model.Barcode),
                CsvFormat.EscapeField(model.StationNo),
                CsvFormat.EscapeField(model.Model),
                model.TestStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                model.TotalDurationSec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CsvFormat.EscapeField(model.FinalResult),
                model.IsTransmitted.ToString(),
                model.TransmitTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                CsvFormat.EscapeField(model.TransmitMessage)
            });
        }

        private string FormatDetailLine(TestItemModel item)
        {
            return CsvFormat.JoinFields(new[]
            {
                item.Id.ToString(),
                item.TestResultsId.ToString(),
                CsvFormat.EscapeField(item.StepKey),
                CsvFormat.EscapeField(item.TestItem),
                CsvFormat.EscapeField(item.TestValue),
                CsvFormat.EscapeField(item.Unit),
                CsvFormat.EscapeField(item.LCL),
                CsvFormat.EscapeField(item.UCL),
                item.MetricValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                CsvFormat.EscapeField(item.TestResult),
                item.StepStartTime.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        private static TestResultModel ParseMasterLine(string[] parts)
        {
            return new TestResultModel
            {
                Id = long.TryParse(parts[0], out var id) ? id : 0,
                Barcode = parts[1],
                StationNo = parts[2],
                Model = parts[3],
                TestStartTime = DateTime.TryParse(parts[4], out var dt) ? dt : DateTime.MinValue,
                TotalDurationSec = double.TryParse(parts[5], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dur) ? dur : 0,
                FinalResult = parts[6],
                IsTransmitted = parts[7] == "True" || parts[7] == "1",
                TransmitTime = DateTime.TryParse(parts[8], out var tt) ? tt : (DateTime?)null,
                TransmitMessage = parts[9]
            };
        }

        private static TestItemModel ParseDetailLine(string[] parts)
        {
            return new TestItemModel
            {
                Id = long.TryParse(parts[0], out var id) ? id : 0,
                TestResultsId = long.TryParse(parts[1], out var pid) ? pid : 0,
                StepKey = parts[2],
                TestItem = parts[3],
                TestValue = parts[4],
                Unit = parts[5],
                LCL = parts[6],
                UCL = parts[7],
                MetricValue = double.TryParse(parts[8], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var mv) ? mv : (double?)null,
                TestResult = parts[9],
                StepStartTime = DateTime.TryParse(parts[10], out var st) ? st : DateTime.MinValue
            };
        }

        private List<string> ReadAllLines(string path)
        {
            if (!File.Exists(path))
            {
                return new List<string>();
            }

            var content = File.ReadAllText(path, Encoding.UTF8);
            return new List<string>(content.Split(new[] { Environment.NewLine }, StringSplitOptions.None));
        }

        private void AppendLine(string path, string line)
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }

        private void CheckAndRotateFile(string path)
        {
            if (_config.MaxLinesPerFile <= 0 || !File.Exists(path))
            {
                return;
            }

            try
            {
                var lineCount = File.ReadLines(path).Count();
                if (lineCount <= _config.MaxLinesPerFile)
                {
                    return;
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var newPath = path.Replace(".csv", $"_{timestamp}.csv");
                File.Move(path, newPath);
                InitializeFiles();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CsvFileStorageProvider] 文件轮转失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
