using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Events;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Planning;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine;
using ZL.Gear.Engine.Planning;

namespace ZL.Gear.Engine.Tests
{
    [TestFixture]
    public class PlanCompileClosureTests
    {
        [Test]
        public void Compile_WorkflowDefinition内嵌Condition_语法错误failClosed()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "DF1",
                    Command = "DynamicFlow",
                    Enable = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["WorkflowDefinition"] = new Dictionary<string, object>
                        {
                            ["Nodes"] = new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    ["Type"] = "Action",
                                    ["Condition"] = "1 + + 2"
                                }
                            }
                        }
                    }
                }
            };

            var context = new PlanCompileContext
            {
                HandlerRegistry = new StubRegistry(new Dictionary<string, bool> { ["DynamicFlow"] = false }),
                Options = new PlanCompileOptions { StrictMissingHandlerCheck = false },
                WorkflowEvaluator = new WorkflowEvaluator()
            };

            var result = new DefaultPlanCompiler().Compile(steps, context);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == "INVALID_CONDITION"), Is.True);
        }

        [Test]
        public async Task ExecuteAsync_编译成功_发布PlanCompileCompletedEvent()
        {
            var bus = new DefaultEventBus();
            PlanCompileCompletedEvent captured = null;
            using var _ = bus.Subscribe<PlanCompileCompletedEvent>(e => captured = e);

            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithStrictPlanCompile(false)
                .WithHandlers("NoOp", new PlanCompileNoOpHandler())
                .WithCustomServices(s => s.AddSingleton<IEventBus>(bus))
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "S1", Command = "NoOp", Enable = true, EvaluateResult = false }
            };

            var run = await executor.ExecuteAsync(steps, "M", "B", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(run.OverallSuccess, Is.True);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.Success, Is.True);
            Assert.That(captured.PlanHash, Is.Not.Empty);
        }

        [Test]
        public async Task ExecuteAsync_编译失败_发布PlanCompileCompletedEvent且failClosed()
        {
            var bus = new DefaultEventBus();
            PlanCompileCompletedEvent captured = null;
            using var _ = bus.Subscribe<PlanCompileCompletedEvent>(e => captured = e);

            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithStrictPlanCompile(true)
                .WithCustomServices(s => s.AddSingleton<IEventBus>(bus))
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "S1", Command = "UnknownCommand_XYZ", Enable = true }
            };

            var run = await executor.ExecuteAsync(steps, "M", "B", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(run.OverallSuccess, Is.False);
            Assert.That(run.CompileErrors, Is.Not.Empty);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.Success, Is.False);
            Assert.That(captured.Diagnostics.Any(d => d.Code == "MISSING_HANDLER"), Is.True);
        }

        [Test]
        public void PlanCompileCompletedEvent_携带Diagnostics()
        {
            var diagnostics = new List<PlanCompileDiagnostic>
            {
                PlanCompileDiagnostic.Warning("MISSING_HANDLER", "warn", "S1")
            };
            var evt = new PlanCompileCompletedEvent(true, "hash123", diagnostics);
            Assert.IsTrue(evt.Success);
            Assert.AreEqual("hash123", evt.PlanHash);
            Assert.AreEqual(1, evt.Diagnostics.Count);
        }

        private sealed class PlanCompileNoOpHandler : IStepHandler
        {
            public Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
                => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "ok"));
        }

        private sealed class StubRegistry : IStepHandlerRegistry
        {
            public StubRegistry(Dictionary<string, bool> map) => _map = map;
            private readonly Dictionary<string, bool> _map;
            public bool? GetEvaluateResult(string command) => _map.TryGetValue(command, out var v) ? v : (bool?)null;
            public void RegisterHandler(string command, IStepHandler handler, bool allowOverwrite = true) { }
            public void RegisterHandlerWithAction(string command, IStepHandler handler, bool allowOverwrite = true) { }
            public IEnumerable<string> GetRegisteredCommands() => _map.Keys;
            public void SetEvaluateResult(string command, bool evaluateResult) => _map[command] = evaluateResult;
        }
    }
}
