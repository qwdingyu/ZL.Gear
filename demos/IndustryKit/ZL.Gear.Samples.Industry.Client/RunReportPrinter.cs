using System;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Samples.Industry.Client
{
    /// <summary>
    /// 将 <see cref="TestRunResult"/> 打印为可读报告，突出「执行树 ≠ 一步 OverallSuccess」。
    /// </summary>
    internal static class RunReportPrinter
    {
        public static void Print(TestRunResult result)
        {
            if (result == null)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("── 执行报告（SequenceExecutor）──");
            Console.WriteLine($"OverallSuccess : {result.OverallSuccess}");
            Console.WriteLine($"Model / Barcode: {result.Model} / {result.Barcode}");
            Console.WriteLine($"耗时           : {result.TotalDurationSeconds}s");
            Console.WriteLine("步骤树:");

            foreach (var top in result.StepResults)
            {
                AppendStep(top, "  ");
            }

            if (!string.IsNullOrWhiteSpace(result.Summary))
            {
                Console.WriteLine("── Summary ──");
                Console.WriteLine(result.Summary.TrimEnd());
            }
        }

        private static void AppendStep(StepRunResult step, string indent)
        {
            var icon = step.Outcome switch
            {
                StepOutcome.Passed => "✓",
                StepOutcome.Failed => "✗",
                StepOutcome.Skipped => "○",
                _ => "?"
            };

            Console.WriteLine(
                $"{indent}{icon} {step.StepName} [{step.Outcome}] {step.DurationSeconds:F2}s"
                + (string.IsNullOrWhiteSpace(step.Message) ? string.Empty : $" — {step.Message}"));

            foreach (var sub in step.SubStepResults)
            {
                AppendStep(sub, indent + "  ");
            }
        }
    }
}
