using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Extensions.Data.Abstractions;

namespace ZL.Gear.Extensions.Data.Repositories
{
    /// <summary>
    /// 测试数据仓储接口
    /// </summary>
    public interface ITestDataRepository
    {
        /// <summary>
        /// 保存测试结果
        /// </summary>
        Task<bool> SaveAsync(TestResultModel result);

        /// <summary>
        /// 批量保存
        /// </summary>
        Task<int> SaveBatchAsync(IEnumerable<TestResultModel> results);

        /// <summary>
        /// 根据条码查询
        /// </summary>
        Task<IReadOnlyList<TestResultModel>> QueryByBarcodeAsync(string barcode, DateTime? startTime = null, DateTime? endTime = null);

        /// <summary>
        /// 根据时间范围查询
        /// </summary>
        Task<IReadOnlyList<TestResultModel>> QueryByTimeRangeAsync(DateTime startTime, DateTime endTime);

        /// <summary>
        /// 分页查询
        /// </summary>
        Task<(IReadOnlyList<TestResultModel> Items, int Total)> QueryPagedAsync(int page, int pageSize, string? barcode = null, string? model = null);

        /// <summary>
        /// 获取待上传数据
        /// </summary>
        Task<IReadOnlyList<TestResultModel>> GetPendingUploadsAsync(int batchSize = 20);

        /// <summary>
        /// 更新上传状态
        /// </summary>
        Task<bool> UpdateTransmitStatusAsync(long id, bool success, string? message);

        /// <summary>
        /// 按条件统计
        /// </summary>
        Task<Dictionary<string, object>> GetStatisticsAsync(DateTime startTime, DateTime endTime, string? stationNo = null);
    }
}
