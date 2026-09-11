using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// ZL.Gear 引擎的简易入口点。
    /// 给用户提供最简单的一行代码调用体验。
    /// </summary>
    public class GearRunner
    {
        private readonly SequenceExecutor _executor;

        public GearRunner(SequenceExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        /// <summary>
        /// 执行测试序列。
        /// </summary>
        /// <param name="model">产品型号</param>
        /// <param name="barcode">产品条码</param>
        /// <param name="steps">步骤列表</param>
        /// <param name="progress">进度反馈接口</param>
        /// <param name="token">取消令牌</param>
        /// <returns>测试运行结果</returns>
        public async Task<TestRunResult> RunAsync(
            string model, 
            string barcode, 
            List<StepConfig> steps, 
            IProgress<StepRunResult> progress = null, 
            CancellationToken token = default)
        {
            return await _executor.ExecuteAsync(steps, model, barcode, new Dictionary<string, object>(), token, progress);
        }
    }
}
