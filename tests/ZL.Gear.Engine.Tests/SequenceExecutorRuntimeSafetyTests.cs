using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Engine;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine.Tests
{
    [TestFixture]
    public class SequenceExecutorRuntimeSafetyTests
    {
        private sealed class SlowStepHandler : IStepHandler
        {
            private readonly int _delayMs;

            public SlowStepHandler(int delayMs) => _delayMs = delayMs;

            public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
            {
                await Task.Delay(_delayMs, context.CancellationToken).ConfigureAwait(false);
                return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "done");
            }
        }

        private sealed class IgnoringCancellationHandler : IStepHandler
        {
            public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
            {
                await Task.Delay(TimeSpan.FromMinutes(10)).ConfigureAwait(false);
                return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "should not reach");
            }
        }

        private sealed class TagStepHandler : IStepHandler
        {
            private readonly string _runtimeTag;

            public TagStepHandler(string runtimeTag) => _runtimeTag = runtimeTag;

            public Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
            {
                return Task.FromResult<ExecutionResultBase>(
                    ExecutionResult<List<Measurement>>.Succeeded(
                        new List<Measurement>(),
                        1,
                        _runtimeTag));
            }
        }

        [Test]
        public void ExecuteAsync_同实例并发第二次调用抛出InvalidOperationException()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithHandlers("SlowStep", new SlowStepHandler(800))
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    StepName = "slow",
                    Command = "SlowStep",
                    Enable = true
                }
            };

            var first = executor.ExecuteAsync(
                steps,
                "MODEL",
                "BAR-1",
                new Dictionary<string, object>(),
                CancellationToken.None);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await executor.ExecuteAsync(
                    steps,
                    "MODEL",
                    "BAR-2",
                    new Dictionary<string, object>(),
                    CancellationToken.None).ConfigureAwait(false));

            first.GetAwaiter().GetResult();
        }

        [Test]
        public async Task ExecuteAsync_不修改调用方Plan对象()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithHandlers("NoOp", new SlowStepHandler(0))
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    StepName = "noop",
                    Command = "NoOp",
                    Target = "LogicalDeviceRole",
                    Enable = true
                }
            };

            const string originalTarget = "LogicalDeviceRole";
            await executor.ExecuteAsync(
                steps,
                "MODEL",
                "BAR-PLAN",
                new Dictionary<string, object>(),
                CancellationToken.None);

            Assert.AreEqual(originalTarget, steps[0].Target);
        }

        [Test]
        public async Task DispatchSingleAsync_忽略CancellationToken时在TimeoutMs内返回Failed()
        {
            var lookup = new RegistryStepHandlerLookup(new DefaultStepHandlerFactory());
            var registry = new SimpleActionRegistry();
            var pipeline = new StepPipelineBuilder().Build();
            var dispatcher = new StepDispatcher(
                lookup,
                new DefaultStepHandlerFactory(),
                registry,
                pipeline,
                _ => { },
                enableUnknownCommandWarning: false,
                defaultTimeoutMs: 30_000,
                BuiltInModules.Core);

            dispatcher.RegisterHandler("HangForever", new IgnoringCancellationHandler());

            var step = new StepConfig
            {
                StepKey = "H1",
                StepName = "hang",
                Command = "HangForever",
                TimeoutMs = 200,
                Enable = true
            };

            var services = new ServiceCollection();
            services.AddSingleton<Action<string>>(_ => { });
            var sp = services.BuildServiceProvider();

            var context = new StepContext(
                step.StepKey,
                step,
                new ReadOnlyDictionary<string, ZL.Gear.Core.Devices.Abstractions.IDevice>(new Dictionary<string, ZL.Gear.Core.Devices.Abstractions.IDevice>()),
                sp,
                CancellationToken.None,
                RunTestMode.Auto,
                new ContextVariableStore());

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await dispatcher.DispatchSingleAsync(step, context).ConfigureAwait(false);
            sw.Stop();

            Assert.IsFalse(result.Success);
            Assert.Less(sw.ElapsedMilliseconds, 2000, "步骤超时应让引擎在 TimeoutMs 附近返回，而非无限等待");
            Assert.That(result.Message, Does.Contain("超时").Or.Contain("Timeout"));
        }

        [Test]
        public async Task ExecuteAsync_分配唯一RunId()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithHandlers("NoOp", new SlowStepHandler(0))
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "S1", StepName = "noop", Command = "NoOp", Enable = true }
            };

            var result = await executor.ExecuteAsync(
                steps, "MODEL", "BAR", new Dictionary<string, object>(), CancellationToken.None);

            Assert.AreNotEqual(Guid.Empty, result.RunId);
        }

        [Test]
        public async Task 两个独立Runtime_同进程并发_各自Handler不串用()
        {
            using var runtimeA = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithHandlers("Tag", new TagStepHandler("RUNTIME_A"))
                .Build();

            using var runtimeB = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithHandlers("Tag", new TagStepHandler("RUNTIME_B"))
                .Build();

            var stepsA = new List<StepConfig>
            {
                new StepConfig { StepKey = "S1", StepName = "tag", Command = "Tag", Enable = true, EvaluateResult = false }
            };
            var stepsB = new List<StepConfig>
            {
                new StepConfig { StepKey = "S1", StepName = "tag", Command = "Tag", Enable = true, EvaluateResult = false }
            };

            var taskA = runtimeA.ExecuteAsync(stepsA, "M", "A", new Dictionary<string, object>(), CancellationToken.None);
            var taskB = runtimeB.ExecuteAsync(stepsB, "M", "B", new Dictionary<string, object>(), CancellationToken.None);

            var results = await Task.WhenAll(taskA, taskB);

            Assert.AreNotEqual(results[0].RunId, results[1].RunId);
            Assert.AreEqual("RUNTIME_A", results[0].StepResults[0].Message);
            Assert.AreEqual("RUNTIME_B", results[1].StepResults[0].Message);
        }

        [Test]
        public async Task Stop_按RunId_仅取消匹配的Run()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithHandlers("SlowStep", new SlowStepHandler(3000))
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "S1", StepName = "slow", Command = "SlowStep", Enable = true }
            };

            var runTask = executor.ExecuteAsync(
                steps, "MODEL", "BAR", new Dictionary<string, object>(), CancellationToken.None);

            await Task.Delay(100);
            var runId = executor.ActiveRunId;
            Assert.IsNotNull(runId);

            executor.Stop(Guid.NewGuid());
            Assert.IsFalse(runTask.IsCompleted, "错误 RunId 不应取消当前 Run");

            executor.Stop(runId.Value);
            var result = await runTask;
            Assert.IsFalse(result.OverallSuccess);
        }
    }
}
