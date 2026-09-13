using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Engine;

namespace ZL.Gear.Engine.Tests
{
    /// <summary>
    /// DeviceKeyResolver 行为闭环：Condition 不得当设备键；嵌套 Target / 角色映射仍生效。
    /// </summary>
    [TestFixture]
    public class DeviceKeyResolverClosureTests
    {
        [Test]
        public async Task Parameters_Condition表达式_不触发设备租约()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithStrictPlanCompile(false)
                .WithHandlers("NoOp", new DeviceKeyTestNoOpHandler())
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
            Assert.That(run.StepResults, Has.Count.EqualTo(1));
            Assert.That(run.StepResults[0].Outcome, Is.EqualTo(StepOutcome.Skipped));
            Assert.That(run.OverallSuccess, Is.True);
        }

        [Test]
        public async Task Parameters_WorkflowDefinition嵌套Target_仍租用对应设备()
        {
            var deviceService = new DeviceKeyTrackingDeviceService();
            using var executor = SequenceExecutorBuilder.Create()
                .AsInstrumentedHost(deviceService)
                .WithStrictPlanCompile(false)
                .WithHandlers("NoOp", new DeviceKeyTestNoOpHandler())
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "NoOp",
                    Enable = true,
                    EvaluateResult = false,
                    Parameters = new Dictionary<string, object>
                    {
                        ["WorkflowDefinition"] = new Dictionary<string, object>
                        {
                            ["Nodes"] = new List<object>
                            {
                                new Dictionary<string, object> { ["Target"] = "plc_nested" }
                            }
                        }
                    }
                }
            };

            var run = await executor.ExecuteAsync(steps, "M", "B", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(run.OverallSuccess, Is.True);
            Assert.That(deviceService.LeasedKeys, Contains.Item("plc_nested"));
        }

        [Test]
        public async Task 设备角色映射_租约使用物理设备键()
        {
            var deviceService = new DeviceKeyTrackingDeviceService();
            var roles = new Dictionary<string, object> { ["Scanner"] = "Keyence_Fixed_01" };

            using var executor = SequenceExecutorBuilder.Create()
                .AsInstrumentedHost(deviceService)
                .WithProfileService(new SimpleGearProfileService(roles))
                .WithStrictPlanCompile(false)
                .WithHandlers("NoOp", new DeviceKeyTestNoOpHandler())
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "NoOp",
                    Target = "Scanner",
                    Enable = true,
                    EvaluateResult = false
                }
            };

            await executor.ExecuteAsync(steps, "M", "B", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(deviceService.LeasedKeys, Contains.Item("Keyence_Fixed_01"));
            Assert.That(deviceService.LeasedKeys, Does.Not.Contain("Scanner"));
        }

        private sealed class DeviceKeyTestNoOpHandler : IStepHandler
        {
            public Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
                => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "ok"));
        }

        private sealed class DeviceKeyTrackingDeviceService : IDeviceService
        {
            public List<string> LeasedKeys { get; } = new List<string>();

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
                LeasedKeys.Add(deviceKey);
                return Task.FromResult<IDeviceLease<TDevice>>(new FakeLease<TDevice>((TDevice)(IDevice)new FakeDevice()));
            }

            public Task InitializeAllDevicesAsync(int maxParallelism = 4, CancellationToken token = default) => Task.CompletedTask;
            public Task EmergencyStopAsync(CancellationToken token = default) => Task.CompletedTask;
        }
    }
}
