using System;
using System.Collections.Generic;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Core.Runner
{
    public class TestRunResult
    {

        public string Model { get; set; }
        public string Barcode { get; set; }
        /// <summary>
        /// 指示整个测试序列是否所有步骤都成功通过。
        /// </summary>
        public bool OverallSuccess { get; set; }
        /// <summary>
        /// 本次测试运行的总体摘要信息。
        /// </summary>
        public string Summary { get; set; }
        /// <summary>
        /// 测试开始时间。
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// 测试结束时间。
        /// </summary>
        public DateTime EndTime { get; set; }
        /// <summary>
        /// 总耗时（秒）。
        /// </summary>
        public string TotalDurationSeconds => ((EndTime - StartTime).TotalSeconds).ToString("F2");
        /// <summary>
        /// 包含所有步骤执行结果的树状结构，用于最终保存和详细分析
        /// </summary>
        public List<StepRunResult> StepResults { get; set; } = new List<StepRunResult>();

    }
}
