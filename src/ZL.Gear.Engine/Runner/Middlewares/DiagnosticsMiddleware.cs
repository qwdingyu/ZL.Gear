using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Devices;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 脚本自诊断中间件 (Cycle Time Analyzer)
    /// 用于记录步骤执行时长，分析通讯瓶颈，优化产线节拍
    /// </summary>
    public class DiagnosticsMiddleware : IStepMiddleware
    {
        /// <summary>
        /// 每个命令保留的最近样本数上限（固定窗口），防止产线长稳运行时无限增长。
        /// </summary>
        private const int MaxSamplesPerCommand = 1000;

        private static readonly ConcurrentDictionary<string, ConcurrentQueue<double>> _stats = new();
        private readonly Action<string> _log;
        private readonly IMetrics _metrics;
        private readonly IActivity _activity;

        public DiagnosticsMiddleware(Action<string> log, IMetrics metrics = null, IActivity activity = null)
        {
            _log = log;
            _metrics = metrics;
            _activity = activity;
        }

        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(
            StepConfig step,
            StepContext context,
            Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            var activityTags = new (string Key, string Value)[]
            {
                ("step", step.StepName ?? string.Empty),
                ("command", step.Command ?? string.Empty)
            };

            using var activityScope = _activity?.StartActivity("step.execute", activityTags);
            var sw = Stopwatch.StartNew();
            ExecutionResult<List<Measurement>> result;
            try
            {
                result = await next(step, context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _metrics?.Record("step.errors", 1, ("command", step.Command ?? "Unknown"));
                throw;
            }
            sw.Stop();

            var duration = sw.Elapsed.TotalMilliseconds;
            RecordStat(step.Command ?? "Unknown", duration);
            _metrics?.Record("step.duration", duration, ("command", step.Command ?? "Unknown"));
            _metrics?.Record("step.success", result.Success ? 1 : 0, ("command", step.Command ?? "Unknown"));

            // 如果单步执行时间过长，记录诊断警报
            if (duration > 1000 && step.Command != "WaitUntil" && step.Command != "PlcDelay")
            {
                _log($"[诊断警报] 步骤 '{step.StepName}' ({step.Command}) 耗时 {duration:F2}ms，超出优化阈值！");
            }

            return result;
        }

        private void RecordStat(string command, double ms)
        {
            var queue = _stats.GetOrAdd(command, _ => new ConcurrentQueue<double>());
            queue.Enqueue(ms);

            // 固定窗口：超过上限时淘汰最旧样本，内存有界（O(命令数 × 窗口)）。
            while (queue.Count > MaxSamplesPerCommand)
            {
                queue.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 生成诊断报告（基于固定窗口内的样本聚合）。
        /// </summary>
        public static string GenerateReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("           ZL.Gear 脚本自诊断报告 (Cycle Time)");
            sb.AppendLine("==================================================");
            sb.AppendLine($"| {"命令类型",-15} | {"调用次数",-8} | {"平均耗时(ms)",-12} | {"最大耗时(ms)",-12} |");
            sb.AppendLine("|-----------------|----------|--------------|--------------|");

            foreach (var kvp in _stats)
            {
                var samples = kvp.Value.ToArray();
                if (samples.Length == 0) continue;

                var avg = samples.Average();
                var max = samples.Max();
                sb.AppendLine($"| {kvp.Key,-15} | {samples.Length,-8} | {avg,-12:F2} | {max,-12:F2} |");
            }
            sb.AppendLine("==================================================");
            return sb.ToString();
        }
    }
}
