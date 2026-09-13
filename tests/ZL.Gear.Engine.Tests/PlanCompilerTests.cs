using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Planning;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine;
using ZL.Gear.Engine.Planning;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine.Tests
{
    [TestFixture]
    public class PlanCompilerTests
    {
        private sealed class StubRegistry : IStepHandlerRegistry
        {
            private readonly Dictionary<string, bool> _evaluate;

            public StubRegistry(Dictionary<string, bool> evaluate) => _evaluate = evaluate;

            public bool? GetEvaluateResult(string command)
                => _evaluate.TryGetValue(command, out var v) ? v : (bool?)null;

            public void RegisterHandler(string command, IStepHandler handler, bool allowOverwrite = true) { }

            public void RegisterHandlerWithAction(string command, IStepHandler handler, bool allowOverwrite = true) { }

            public IEnumerable<string> GetRegisteredCommands() => _evaluate.Keys;

            public void SetEvaluateResult(string command, bool evaluateResult) => _evaluate[command] = evaluateResult;
        }

        private static PlanCompileContext LenientContext(IStepHandlerRegistry registry = null)
            => new PlanCompileContext
            {
                HandlerRegistry = registry,
                Options = new PlanCompileOptions { StrictMissingHandlerCheck = false }
            };

        [Test]
        public void Compile_空计划_failClosed()
        {
            var result = new DefaultPlanCompiler().Compile(new List<StepConfig>(), new PlanCompileContext());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == "PLAN_EMPTY"), Is.True);
        }

        [Test]
        public void Compile_重复StepKey_failClosed()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "A", Command = "Cmd1", Enable = true },
                new StepConfig { StepKey = "a", Command = "Cmd2", Enable = true }
            };

            var result = new DefaultPlanCompiler().Compile(steps, LenientContext());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == "DUPLICATE_STEP_KEY"), Is.True);
        }

        [Test]
        public void Compile_成功_回填EvaluateResult且不污染源Plan()
        {
            var source = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "MyCmd",
                    Enable = true,
                    EvaluateResult = null
                }
            };

            var registry = new StubRegistry(new Dictionary<string, bool> { ["MyCmd"] = true });
            var result = new DefaultPlanCompiler().Compile(source, LenientContext(registry));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Plan.Steps[0].EvaluateResult, Is.True);
            Assert.That(source[0].EvaluateResult, Is.Null);
        }

        [Test]
        public void Compile_未知命令_Strict默认失败()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "S1", Command = "NotRegisteredAnywhere", Enable = true }
            };

            var result = new DefaultPlanCompiler().Compile(steps, new PlanCompileContext
            {
                HandlerRegistry = new StubRegistry(new Dictionary<string, bool>()),
                Options = new PlanCompileOptions { StrictMissingHandlerCheck = true }
            });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == "MISSING_HANDLER"), Is.True);
        }

        [Test]
        public void Compile_未知命令_非Strict仅Warning()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "S1", Command = "NotRegisteredAnywhere", Enable = true }
            };

            var result = new DefaultPlanCompiler().Compile(steps, LenientContext(new StubRegistry(new Dictionary<string, bool>())));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Diagnostics.Any(d => d.Code == "MISSING_HANDLER" && d.Level == PlanCompileDiagnosticLevel.Warning), Is.True);
        }

        [Test]
        public void Compile_无效Condition语法_failClosed()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "X",
                    Enable = true,
                    Parameters = new Dictionary<string, object> { ["Condition"] = "1 + + 2" }
                }
            };

            var context = LenientContext(new StubRegistry(new Dictionary<string, bool> { ["X"] = false }));
            context.WorkflowEvaluator = new WorkflowEvaluator();
            var result = new DefaultPlanCompiler().Compile(steps, context);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == "INVALID_CONDITION"), Is.True);
        }

        [Test]
        public void Compile_相同输入PlanHash稳定()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "S1", Command = "X", Enable = true, EvaluateResult = false }
            };

            var ctx = LenientContext(new StubRegistry(new Dictionary<string, bool> { ["X"] = false }));
            var h1 = new DefaultPlanCompiler().Compile(steps, ctx).Plan.PlanHash;
            var h2 = new DefaultPlanCompiler().Compile(steps, ctx).Plan.PlanHash;

            Assert.That(h1, Is.EqualTo(h2));
        }

        [SetUp]
        public void ResetNeverRunCounter() => PlanCompilerTests_NeverRunHandler.RunCount = 0;

        [Test]
        public async Task ExecuteAsync_编译失败_不执行步骤且OverallSuccess为False()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithHandlers("NeverRun", new PlanCompilerTests_NeverRunHandler())
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "dup", Command = "NeverRun", Enable = true },
                new StepConfig { StepKey = "DUP", Command = "NeverRun", Enable = true }
            };

            var run = await executor.ExecuteAsync(
                steps, "M", "B", new Dictionary<string, object>(), CancellationToken.None);

            Assert.That(run.OverallSuccess, Is.False);
            Assert.That(run.CompileErrors, Is.Not.Empty);
            Assert.That(PlanCompilerTests_NeverRunHandler.RunCount, Is.Zero);
        }

        [Test]
        public async Task ExecuteAsync_编译失败后ActiveRunId已清除()
        {
            using var executor = SequenceExecutorBuilder.Create().AsLogicOnlyDemoHost().Build();

            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "x", Command = "C1", Enable = true },
                new StepConfig { StepKey = "X", Command = "C2", Enable = true }
            };

            await executor.ExecuteAsync(steps, "M", "B", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(executor.ActiveRunId, Is.Null);
        }

        [Test]
        public async Task ExecuteAsync_编译成功_写入PlanHash与Warnings()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithStrictPlanCompile(false)
                .WithHandlers("Ok", new PlanCompilerTests_OkHandler())
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "Ok",
                    Enable = true,
                    EvaluateResult = false,
                    Parameters = new Dictionary<string, object> { ["Condition"] = "true" }
                }
            };

            var run = await executor.ExecuteAsync(
                steps, "M", "B", new Dictionary<string, object>(), CancellationToken.None);

            Assert.That(run.PlanHash, Is.Not.Null.And.Not.Empty);
            Assert.That(run.CompileErrors, Is.Empty);
        }

        [Test]
        public async Task Builder_默认Strict_未注册命令编译失败()
        {
            using var executor = SequenceExecutorBuilder.Create().AsLogicOnlyDemoHost().Build();

            var steps = new List<StepConfig>
            {
                new StepConfig { StepKey = "S1", Command = "DefinitelyMissingCommand", Enable = true }
            };

            var run = await executor.ExecuteAsync(steps, "M", "B", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(run.OverallSuccess, Is.False);
            Assert.That(run.CompileErrors.Any(e => e.Code == "MISSING_HANDLER"), Is.True);
        }
    }

    internal sealed class PlanCompilerTests_NeverRunHandler : IStepHandler
    {
        public static int RunCount;

        public Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            RunCount++;
            return Task.FromResult<ExecutionResultBase>(
                ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "ran"));
        }
    }

    internal sealed class PlanCompilerTests_OkHandler : IStepHandler
    {
        public Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            return Task.FromResult<ExecutionResultBase>(
                ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "ok"));
        }
    }
}
