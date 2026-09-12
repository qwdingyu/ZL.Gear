using System;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Core.Events
{
    public static class TestEvents
    {
        public static Action<string> StepStarted { get; set; }
        public static Action<string> StatusChanged { get; set; }
        public static Action<string, string, object> RealTimeValueChanged { get; set; }
        public static Action<string, string> Log { get; set; }
        public static event Action<StepRunResult> OnStepTreeCompleted;
    }

    public static class RunnerEvents
    {
        public static Action<StepRunResult> OnStepProgress;
        public static Action<TestRunResult> OnTestRunCompleted;
        public static Action<string> OnRunTestErrorTip { get; set; }
        public static Action<StepContext> StepStarted { get; set; }
    }
}
