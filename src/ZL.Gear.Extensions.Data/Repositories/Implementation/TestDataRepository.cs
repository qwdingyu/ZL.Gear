using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.Gear.Extensions.Data.Abstractions;
using ZL.Gear.Extensions.Data.Repositories;

namespace ZL.Gear.Extensions.Data.Repositories.Implementation
{
    public class TestDataRepository : ITestDataRepository
    {
        private readonly IStorageProvider _storageProvider;

        public TestDataRepository(IStorageProvider storageProvider)
        {
            _storageProvider = storageProvider;
        }

        public Task<bool> SaveAsync(TestResultModel result)
        {
            return _storageProvider.SaveAsync(result);
        }

        public Task<int> SaveBatchAsync(IEnumerable<TestResultModel> results)
        {
            return _storageProvider.SaveBatchAsync(results);
        }

        public Task<IReadOnlyList<TestResultModel>> QueryByBarcodeAsync(string barcode, DateTime? startTime = null, DateTime? endTime = null)
        {
            return _storageProvider.QueryByBarcodeAsync(barcode, startTime, endTime);
        }

        public Task<IReadOnlyList<TestResultModel>> QueryByTimeRangeAsync(DateTime startTime, DateTime endTime)
        {
            return _storageProvider.QueryByTimeRangeAsync(startTime, endTime);
        }

        public Task<(IReadOnlyList<TestResultModel> Items, int Total)> QueryPagedAsync(int page, int pageSize, string barcode = null, string model = null)
        {
            return _storageProvider.QueryPagedAsync(page, pageSize, barcode, model);
        }

        public Task<IReadOnlyList<TestResultModel>> GetPendingUploadsAsync(int batchSize = 20)
        {
            return _storageProvider.GetPendingUploadAsync(batchSize);
        }

        public Task<bool> UpdateTransmitStatusAsync(long id, bool success, string message)
        {
            return _storageProvider.UpdateUploadStatusAsync(id, success, message);
        }

        public async Task<Dictionary<string, object>> GetStatisticsAsync(DateTime startTime, DateTime endTime, string stationNo = null)
        {
            var results = await _storageProvider.QueryByTimeRangeAsync(startTime, endTime);

            var stats = new Dictionary<string, object>();

            var filtered = string.IsNullOrEmpty(stationNo)
                ? results
                : results.Where(r => r.StationNo == stationNo);

            var totalCount = filtered.Count();
            var passCount = filtered.Count(r => r.FinalResult == "PASS");

            stats["TotalCount"] = totalCount;
            stats["PassCount"] = passCount;
            stats["FailCount"] = totalCount - passCount;
            stats["PassRate"] = totalCount == 0 ? 0 : (double)passCount / totalCount * 100;
            stats["AvgDurationSec"] = filtered.Any() ? filtered.Average(r => r.TotalDurationSec) : 0;
            stats["MinDurationSec"] = filtered.Any() ? filtered.Min(r => r.TotalDurationSec) : 0;
            stats["MaxDurationSec"] = filtered.Any() ? filtered.Max(r => r.TotalDurationSec) : 0;

            return stats;
        }
    }
}
