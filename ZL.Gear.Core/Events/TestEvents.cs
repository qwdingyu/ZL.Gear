using System;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Core.Events
{
    public static class TestEvents
    {
        // 单步开始
        public static Action<string> StepStarted { get; set; }

        //// 单步完成（参数：步骤名，是否成功，耗时s，输出字典）
        //public static Action<string, double, ExecutionResultBase, EvaluationResult> StepCompleted { get; set; }

        //// 总体完成
        //public static Action<SeatResults> TestCompleted { get; set; }

        // 状态变化（比如 Running / Stopped / Error）
        public static Action<string> StatusChanged { get; set; }
        public static Action<string, double> RealTimeValueChanged { get; set; }
        // 通用日志（也可直接用 UiLogManager）
        public static Action<string, string> Log { get; set; } // (level, message)
        /// <summary>
        /// 【新】当一个顶层步骤（及其所有子步骤）完成时触发，携带完整的层级结果树。
        /// UI层应该订阅此事件来更新界面。
        /// </summary>
        public static event Action<StepRunResult> OnStepTreeCompleted;

    }
    public static class RunnerEvents
    {
        /// <summary>
        /// 当任何一个步骤的状态发生变化时触发（开始执行、执行完毕）。
        /// 发布的 StepRunResult 对象包含了该步骤的完整快照信息。
        /// </summary>
        public static  Action<StepRunResult> OnStepProgress;
        /// <summary>
        /// 当整个测试序列（TestRun）执行完成时触发。
        /// </summary>
        public static  Action<TestRunResult> OnTestRunCompleted;


        public static Action<string> OnRunTestErrorTip { get; set; }


        /// <summary>
        ///  单步开始
        /// </summary>
        public static Action<StepContext> StepStarted { get; set; }
    }
}
