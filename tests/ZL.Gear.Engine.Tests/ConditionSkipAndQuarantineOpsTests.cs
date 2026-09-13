using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Engine;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine.Tests
{
    [TestFixture]
    public class ConditionSkipAndQuarantineOpsTests
    {
        private sealed class NoOpHandler : IStepHandler
        {
            public Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
            {
                return Task.FromResult<ExecutionResultBase>(
                    ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "ran"));
            }
        }

        [Test]
        public async Task Condition未满足_步骤Outcome为Skipped而非Passed()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithStrictPlanCompile(false)
                .WithHandlers("NoOp", new NoOpHandler())
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "NoOp",
                    Enable = true,
                    EvaluateResult = false,
                    Parameters = new Dictionary<string, object> { ["Condition"] = "false" }
                }
            };

            var run = await executor.ExecuteAsync(steps, "M", "B", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(run.StepResults[0].Outcome, Is.EqualTo(StepOutcome.Skipped));
            Assert.That(run.StepResults[0].VerdictKind, Is.EqualTo(StepVerdictKind.Skipped));
            Assert.That(run.OverallSuccess, Is.True);
            Assert.That(run.RunVerdictKind, Is.EqualTo(StepVerdictKind.Passed));
        }

        [Test]
        public async Task ClearDeviceQuarantine_超时后可人工复位并重跑()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsInstrumentedHost(new QuarantineTestFakeDeviceService())
                .WithStrictPlanCompile(false)
                .WithHandlers("HangForever", new QuarantineTestHangHandler())
                .WithHandlers("NoOp", new NoOpHandler())
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "HangForever",
                    TimeoutMs = 120,
                    Target = "plc_1",
                    Enable = true,
                    EvaluateResult = false
                }
            };

            var first = await executor.ExecuteAsync(steps, "M", "B1", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(first.QuarantinedDeviceKeys, Is.Not.Empty);
            Assert.That(executor.GetQuarantinedDeviceKeys(), Is.Not.Empty);

            executor.ClearDeviceQuarantine();
            Assert.That(executor.GetQuarantinedDeviceKeys(), Is.Empty);

            var retrySteps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S2",
                    Command = "NoOp",
                    Enable = true,
                    EvaluateResult = false
                }
            };

            var retry = await executor.ExecuteAsync(retrySteps, "M", "B2", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(retry.OverallSuccess, Is.True);
            Assert.That(retry.StepResults[0].Outcome, Is.EqualTo(StepOutcome.Passed));
            Assert.That(retry.Summary, Does.Not.Contain("隔离"));
        }

        [Test]
        public async Task WithClearDeviceQuarantineOnRunStart_第二次Run自动清隔离()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsInstrumentedHost(new QuarantineTestFakeDeviceService())
                .WithStrictPlanCompile(false)
                .WithClearDeviceQuarantineOnRunStart()
                .WithHandlers("HangForever", new QuarantineTestHangHandler())
                .WithHandlers("NoOp", new NoOpHandler())
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "HangForever",
                    TimeoutMs = 120,
                    Target = "plc_1",
                    Enable = true,
                    EvaluateResult = false
                }
            };

            await executor.ExecuteAsync(steps, "M", "B1", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(executor.GetQuarantinedDeviceKeys(), Is.Not.Empty);

            var passSteps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S2",
                    Command = "NoOp",
                    Enable = true,
                    EvaluateResult = false
                }
            };

            var second = await executor.ExecuteAsync(passSteps, "M", "B2", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(second.OverallSuccess, Is.True);
            Assert.That(executor.GetQuarantinedDeviceKeys(), Is.Empty);
            Assert.That(second.Summary, Does.Not.Contain("隔离"));
        }
    }

    internal sealed class QuarantineTestHangHandler : IStepHandler
    {
        public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            await Task.Delay(TimeSpan.FromMinutes(10), context.CancellationToken).ConfigureAwait(false);
            return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "never");
        }
    }

    internal sealed class QuarantineTestFakeDeviceService : IDeviceService
    {
        private sealed class FakeDevice : IDevice
        {
            public string DeviceName => "fake";
            public Action<string> Log { get; set; } = _ => { };
            public bool IsHealthy => true;
            public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
            public Task<DeviceReading> ExecuteAsync(string command, Dictionary<string, object> args, StepContext context)
                => Task.FromResult(new DeviceReading());
        }

        private sealed class FakeLease<TDevice> : IDeviceLease<TDevice> where TDevice : class, IDevice
        {
            public FakeLease(TDevice device) => Device = device;
            public TDevice Device { get; }
            public void Dispose() { }
        }

        public Task<IDeviceLease<TDevice>> LeaseAsync<TDevice>(string deviceKey, CancellationToken token = default)
            where TDevice : class, IDevice
        {
            return Task.FromResult<IDeviceLease<TDevice>>(new FakeLease<TDevice>((TDevice)(IDevice)new FakeDevice()));
        }

        public Task InitializeAllDevicesAsync(int maxParallelism = 4, CancellationToken token = default) => Task.CompletedTask;
        public Task EmergencyStopAsync(CancellationToken token = default) => Task.CompletedTask;
    }
}
