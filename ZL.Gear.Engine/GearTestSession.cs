using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// 管理单次测试会话，支持 WebAPI 异步状态查询。
    /// </summary>
    public class GearTestSession
    {
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public string Model { get; }
        public string Barcode { get; }
        public TestExecutionStatus Status { get; private set; } = TestExecutionStatus.Pending;
        public List<StepRunResult> Progress { get; } = new List<StepRunResult>();
        public TestRunResult Result { get; private set; }
        public string ErrorMessage { get; private set; }

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly GearRunner _runner;

        public GearTestSession(GearRunner runner, string model, string barcode)
        {
            _runner = runner;
            Model = model;
            Barcode = barcode;
        }

        public async Task StartAsync(List<StepConfig> steps)
        {
            Status = TestExecutionStatus.Running;
            var progress = new Progress<StepRunResult>(r => Progress.Add(r));

            try
            {
                Result = await _runner.RunAsync(Model, Barcode, steps, progress, _cts.Token);
                Status = TestExecutionStatus.Completed;
            }
            catch (OperationCanceledException)
            {
                Status = TestExecutionStatus.Cancelled;
            }
            catch (Exception ex)
            {
                Status = TestExecutionStatus.Failed;
                ErrorMessage = ex.Message;
            }
        }

        public void Cancel() => _cts.Cancel();
    }

    public enum TestExecutionStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }
}
