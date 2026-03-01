using System;
using System.Collections.Generic;
using System.Threading;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 代表一次完整的测试请求。这是一个不可变的数据传输对象 (DTO)。
    /// </summary>
    public sealed class TestRequest
    {
        public List<StepConfig> Steps { get; }
        public string Barcode { get; }
        public CancellationToken CancellationToken { get; }
        public Action<StepRunResult> StepProgressReporter { get; }
        public Action<TestRunResult> TestCompletedReporter { get; }

        public readonly Action<string> Log;


        /// <summary>
        /// 【关键】用于将最终的测试结果回调给发起方。
        /// 使用 IProgress<T> 可以安全地将结果从后台线程封送到UI线程。
        /// </summary>
        public IProgress<TestRunResult> ResultReporter { get; }

        public TestRequest(List<StepConfig> steps, string barcode, Action<string> log, CancellationToken cancellationToken = default, Action<StepRunResult> stepProgressReporter = null,
                              Action<TestRunResult> testCompletedReporter = null,
                              IProgress<TestRunResult> resultReporter = null)
        {
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
            Barcode = barcode;
            Log = log ?? (message => { });
            CancellationToken = cancellationToken;
            StepProgressReporter = stepProgressReporter;
            TestCompletedReporter = testCompletedReporter;
            ResultReporter = resultReporter; // 允许为空
        }
    }
}
