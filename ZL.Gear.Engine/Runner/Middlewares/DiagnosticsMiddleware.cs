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
        private static readonly ConcurrentDictionary<string, List<double>> _stats = new();
        private readonly Action<string> _log;

        public DiagnosticsMiddleware(Action<string> log)
        {
            _log = log;
        }

        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(
            StepConfig step, 
            StepContext context, 
            Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            var sw = Stopwatch.StartNew();
            var result = await next(step, context);
            sw.Stop();

            var duration = sw.Elapsed.TotalMilliseconds;
            RecordStat(step.Command ?? "Unknown", duration);

            // 如果单步执行时间过长，记录诊断警报
            if (duration > 1000 && step.Command != "WaitUntil" && step.Command != "PlcDelay")
            {
                _log($"[诊断警报] 步骤 '{step.StepName}' ({step.Command}) 耗时 {duration:F2}ms，超出优化阈值！");
            }

            return result;
        }

        private void RecordStat(string command, double ms)
        {
            var list = _stats.GetOrAdd(command, _ => new List<double>());
            lock (list)
            {
                list.Add(ms);
            }
        }

        /// <summary>
        /// 生成诊断报告
        /// </summary>
        public static string GenerateReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("           ZL.Gear 脚本自诊断报告 (Cycle Time)");
            sb.AppendLine("==================================================");
            sb.AppendLine($"| {"命令类型",-15} | {"调用次数",-8} | {"平均耗时(ms)",-12} | {"最大耗时(ms)",-12} |");
            sb.AppendLine("|-----------------|----------|--------------|--------------|");

            foreach (var kvp in _stats.OrderByDescending(x => x.Value.Average()))
            {
                var avg = kvp.Value.Average();
                var max = kvp.Value.Max();
                sb.AppendLine($"| {kvp.Key,-15} | {kvp.Value.Count,-8} | {avg,-12:F2} | {max,-12:F2} |");
            }
            sb.AppendLine("==================================================");
            return sb.ToString();
        }
    }
}
