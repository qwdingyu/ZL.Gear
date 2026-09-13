using System;
using System.Collections.Generic;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Planning;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Runner;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// 测试执行整体状态
    /// </summary>
    public enum RunState
    {
        Idle,
        Testing,
        Paused,
        Stopping,
        Stopped,
        Error
    }

    /// <summary>
    /// 步骤执行状态变更事件
    /// </summary>
    public class StepProgressEvent : BaseEvent
    {
        public StepRunResult Result { get; }
        public StepProgressEvent(StepRunResult result) => Result = result;
    }

    /// <summary>
    /// 整个测试运行完成事件
    /// </summary>
    public class TestRunCompletedEvent : BaseEvent
    {
        public TestRunResult Result { get; }
        public TestRunCompletedEvent(TestRunResult result) => Result = result;
    }

    /// <summary>
    /// 运行引擎状态变更 (如 Idle -> Testing)
    /// </summary>
    public class RunStateChangedEvent : BaseEvent
    {
        public RunState State { get; }
        public string Message { get; }
        public RunStateChangedEvent(RunState state, string msg = "")
        {
            State = state;
            Message = msg;
        }
    }

    /// <summary>
    /// 运行错误提示 (非步骤内失败，如脚本加载失败、设备未就绪)
    /// </summary>
    public class RunErrorEvent : BaseEvent
    {
        public string Message { get; }
        public RunErrorEvent(string msg) => Message = msg;
    }

    /// <summary>
    /// 计划编译完成（成功或失败均发布，供 MES / UI 消费结构化诊断）。
    /// </summary>
    public class PlanCompileCompletedEvent : BaseEvent
    {
        public PlanCompileCompletedEvent(
            bool success,
            string planHash,
            IList<PlanCompileDiagnostic> diagnostics)
        {
            Success = success;
            PlanHash = planHash ?? string.Empty;
            Diagnostics = diagnostics ?? Array.Empty<PlanCompileDiagnostic>();
        }

        public bool Success { get; }

        public string PlanHash { get; }

        public IList<PlanCompileDiagnostic> Diagnostics { get; }
    }
}
