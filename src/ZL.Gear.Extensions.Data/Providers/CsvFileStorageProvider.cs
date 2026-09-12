using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZL.Gear.Extensions.Data.Abstractions;
using ZL.Gear.Extensions.Data.Configuration;

namespace ZL.Gear.Extensions.Data.Providers
{
    public class CsvFileStorageProvider : IStorageProvider
    {
        private readonly CsvConfiguration _config;
        private readonly string _masterFilePath;
        private readonly string _detailFilePath;
        private readonly object _lockObj = new object();
        private bool _disposed;

        public Enums.StorageType StorageType => Enums.StorageType.CsvFile;

        public CsvFileStorageProvider(CsvConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Directory.CreateDirectory(_config.Directory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _masterFilePath = Path.Combine(_config.Directory, $"{_config.FilePrefix}_Master_{timestamp}.csv");
            _detailFilePath = Path.Combine(_config.Directory, $"{_config.FilePrefix}_Detail_{timestamp}.csv");

            InitializeFiles();
        }

        public Task InitializeAsync()
        {
            InitializeFiles();
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
            if (result == null) return Task.FromResult(false);

            try
            {
                CheckAndRotateFile(_masterFilePath);
                CheckAndRotateFile(_detailFilePath);

                var masterLine = FormatMasterLine(result);
                AppendLine(_masterFilePath, masterLine);

                foreach (var item in result.Items)
                {
                    var detailLine = FormatDetailLine(item);
                    AppendLine(_detailFilePath, detailLine);
                }

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CsvFileStorageProvider] 保存失败: {ex.Message}");
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
                var allLines = ReadAllLines(_masterFilePath);
                var dataLines = allLines.Skip(1);

                foreach (var line in dataLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = ParseLine(line);
                    if (parts.Length < 10) continue;

                    if (parts[1] != barcode) continue;

                    if (startTime.HasValue && DateTime.TryParse(parts[4], out var testTime))
                    {
                        if (testTime < startTime.Value) continue;
                    }
                    if (endTime.HasValue && DateTime.TryParse(parts[4], out testTime))
                    {
                        if (testTime > endTime.Value) continue;
                    }

                    var result = ParseMasterLine(parts);
                    result.Items = await QueryDetailsByMasterIdAsync(result.Id);
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CsvFileStorageProvider] 按条码查询失败: {ex.Message}");
            }

            return results;
        }

        public async Task<IReadOnlyList<TestResultModel>> QueryByTimeRangeAsync(DateTime startTime, DateTime endTime)
        {
            var results = new List<TestResultModel>();

            try
            {
                var allLines = ReadAllLines(_masterFilePath);
                var dataLines = allLines.Skip(1);

                foreach (var line in dataLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = ParseLine(line);
                    if (parts.Length < 10) continue;

                    if (!DateTime.TryParse(parts[4], out var testTime)) continue;
                    if (testTime < startTime || testTime > endTime) continue;

                    var result = ParseMasterLine(parts);
                    result.Items = await QueryDetailsByMasterIdAsync(result.Id);
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CsvFileStorageProvider] 按时间范围查询失败: {ex.Message}");
            }

            return results;
        }

        public async Task<(IReadOnlyList<TestResultModel> Items, int Total)> QueryPagedAsync(int page, int pageSize, string barcode = null, string model = null)
        {
            var allResults = new List<TestResultModel>();

            var allLines = ReadAllLines(_masterFilePath);
            var dataLines = allLines.Skip(1);

            foreach (var line in dataLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = ParseLine(line);
                if (parts.Length < 10) continue;

                if (!string.IsNullOrEmpty(barcode) && parts[1] != barcode) continue;
                if (!string.IsNullOrEmpty(model) && parts[3] != model) continue;

                var result = ParseMasterLine(parts);
                result.Items = await QueryDetailsByMasterIdAsync(result.Id);
                allResults.Add(result);
            }

            var total = allResults.Count;
            var pagedItems = allResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return (pagedItems, total);
        }

        public async Task<IReadOnlyList<TestResultModel>> GetPendingUploadAsync(int batchSize = 20)
        {
            var results = new List<TestResultModel>();

            try
            {
                var allLines = ReadAllLines(_masterFilePath);
                var dataLines = allLines.Skip(1).Take(batchSize);

                foreach (var line in dataLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = ParseLine(line);
                    if (parts.Length < 10) continue;

                    if (parts[7] == "True" || parts[7] == "1") continue;

                    var result = ParseMasterLine(parts);
                    result.Items = await QueryDetailsByMasterIdAsync(result.Id);
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CsvFileStorageProvider] 获取待上传数据失败: {ex.Message}");
            }

            return results;
        }

        public Task<bool> UpdateUploadStatusAsync(long id, bool success, string message)
        {
            try
            {
                var lines = ReadAllLines(_masterFilePath).ToList();
                if (lines.Count <= 1) return Task.FromResult(false);

                var sb = new StringBuilder();
                sb.AppendLine(lines[0]);

                for (int i = 1; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = ParseLine(line);
                    if (parts.Length < 10) continue;

                    if (long.TryParse(parts[0], out var recordId) && recordId == id)
                    {
                        parts[7] = success.ToString();
                        parts[8] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        parts[9] = message?.Replace(",", ";") ?? "";
                        line = string.Join(",", parts);
                    }

                    sb.AppendLine(line);
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

        public Task<bool> ExportAsync(string targetPath, DateTime startTime, DateTime endTime)
        {
            try
            {
                var results = QueryByTimeRangeAsync(startTime, endTime).Result;

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

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CsvFileStorageProvider] 导出失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private async Task<List<TestItemModel>> QueryDetailsByMasterIdAsync(long masterId)
        {
            var items = new List<TestItemModel>();

            try
            {
                var allLines = ReadAllLines(_detailFilePath);
                var dataLines = allLines.Skip(1);

                foreach (var line in dataLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = ParseLine(line);
                    if (parts.Length < 11) continue;

                    if (long.TryParse(parts[1], out var pid) && pid == masterId)
                    {
                        items.Add(ParseDetailLine(parts));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CsvFileStorageProvider] 查询子表数据失败: {ex.Message}");
            }

            return items;
        }

        private string FormatMasterLine(TestResultModel model)
        {
            return $"{model.Id},{Escape(model.Barcode)},{Escape(model.StationNo)},{Escape(model.Model)}," +
                   $"{model.TestStartTime:yyyy-MM-dd HH:mm:ss},{model.TotalDurationSec},{Escape(model.FinalResult)}," +
                   $"{model.IsTransmitted},{model.TransmitTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""},{Escape(model.TransmitMessage)}";
        }

        private string FormatDetailLine(TestItemModel item)
        {
            return $"{item.Id},{item.TestResultsId},{Escape(item.StepKey)},{Escape(item.TestItem)}," +
                   $"{Escape(item.TestValue)},{Escape(item.Unit)},{Escape(item.LCL)},{Escape(item.UCL)}," +
                   $"{item.MetricValue?.ToString() ?? ""},{Escape(item.TestResult)},{item.StepStartTime:yyyy-MM-dd HH:mm:ss}";
        }

        private TestResultModel ParseMasterLine(string[] parts)
        {
            return new TestResultModel
            {
                Id = long.TryParse(parts[0], out var id) ? id : 0,
                Barcode = parts[1],
                StationNo = parts[2],
                Model = parts[3],
                TestStartTime = DateTime.TryParse(parts[4], out var dt) ? dt : DateTime.MinValue,
                TotalDurationSec = double.TryParse(parts[5], out var dur) ? dur : 0,
                FinalResult = parts[6],
                IsTransmitted = parts[7] == "True" || parts[7] == "1",
                TransmitTime = DateTime.TryParse(parts[8], out var tt) ? tt : (DateTime?)null,
                TransmitMessage = parts[9]
            };
        }

        private TestItemModel ParseDetailLine(string[] parts)
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
                MetricValue = double.TryParse(parts[8], out var mv) ? mv : (double?)null,
                TestResult = parts[9],
                StepStartTime = DateTime.TryParse(parts[10], out var st) ? st : DateTime.MinValue
            };
        }

        private string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace(",", ";");
        }

        private string[] ParseLine(string line)
        {
            return line.Split(',');
        }

        private List<string> ReadAllLines(string path)
        {
            if (!File.Exists(path)) return new List<string>();
            var content = File.ReadAllText(path, Encoding.UTF8);
            return new List<string>(content.Split(new[] { Environment.NewLine }, StringSplitOptions.None));
        }

        private void AppendLine(string path, string line)
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }

        private void CheckAndRotateFile(string path)
        {
            if (_config.MaxLinesPerFile <= 0) return;

            try
            {
                var lineCount = File.ReadLines(path).Count();
                if (lineCount > _config.MaxLinesPerFile)
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var newPath = path.Replace(".csv", $"_{timestamp}.csv");
                    File.Move(path, newPath);
                    InitializeFiles();
                }
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
