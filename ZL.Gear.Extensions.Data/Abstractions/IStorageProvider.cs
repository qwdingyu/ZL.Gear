using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZL.Gear.Extensions.Data.Abstractions
{
    /// <summary>
    /// 存储提供者接口 - 抽象不同存储介质的统一操作
    /// </summary>
    public interface IStorageProvider : IDisposable
    {
        /// <summary>
        /// 存储类型
        /// </summary>
        Enums.StorageType StorageType { get; }

        /// <summary>
        /// 保存测试结果
        /// </summary>
        Task<bool> SaveAsync(TestResultModel result);

        /// <summary>
        /// 批量保存测试结果
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
        Task<(IReadOnlyList<TestResultModel> Items, int Total)> QueryPagedAsync(int page, int pageSize, string barcode = null, string model = null);

        /// <summary>
        /// 获取待上传数据（用于 MES 上传）
        /// </summary>
        Task<IReadOnlyList<TestResultModel>> GetPendingUploadAsync(int batchSize = 20);

        /// <summary>
        /// 更新上传状态
        /// </summary>
        Task<bool> UpdateUploadStatusAsync(long id, bool success, string message);

        /// <summary>
        /// 导出数据到目标存储（用于迁移或备份）
        /// </summary>
        Task<bool> ExportAsync(string targetPath, DateTime startTime, DateTime endTime);

        /// <summary>
        /// 初始化存储结构
        /// </summary>
        Task InitializeAsync();
    }

    /// <summary>
    /// 测试结果模型 - DTO 与 Entity 的中间层
    /// </summary>
    public class TestResultModel
    {
        public long Id { get; set; }

        public string Barcode { get; set; } = string.Empty;

        public string StationNo { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public DateTime TestStartTime { get; set; }

        public double TotalDurationSec { get; set; }

        public string FinalResult { get; set; } = string.Empty;

        public bool IsTransmitted { get; set; }

        public DateTime? TransmitTime { get; set; }

        public string TransmitMessage { get; set; }

        public List<TestItemModel> Items { get; set; } = new List<TestItemModel>();
    }

    /// <summary>
    /// 测试项模型
    /// </summary>
    public class TestItemModel
    {
        public long Id { get; set; }

        public long TestResultsId { get; set; }

        public string StepKey { get; set; } = string.Empty;

        public string TestItem { get; set; } = string.Empty;

        public string TestValue { get; set; }

        public string Unit { get; set; }

        public string LCL { get; set; }

        public string UCL { get; set; }

        public double? MetricValue { get; set; }

        public string TestResult { get; set; } = string.Empty;

        public DateTime StepStartTime { get; set; }
    }
}
