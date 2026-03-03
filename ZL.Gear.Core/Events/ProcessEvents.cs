using System;
using ZL.Gear.Core.Models;
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
}
