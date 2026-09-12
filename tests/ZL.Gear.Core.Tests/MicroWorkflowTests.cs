using NUnit.Framework;
using System;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Testing.Common;

namespace ZL.Gear.Core.Tests
{
    [TestFixture]
    public class MicroWorkflowTests
    {
        private StepConfig _step;
        private StepContext _context;

        [SetUp]
        public void Setup()
        {
            _step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "TestWorkflow",
                Parameters = new System.Collections.Generic.Dictionary<string, object>()
            };

            _context = StepContextFactory.CreateLogicOnly(_step);
        }

        #region Then 短路逻辑

        [Test]
        public async Task Then_前一步成功_后一步执行()
        {
            var executed = false;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Then("Step1", (s, c) =>
            {
                executed = true;
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
            });

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
            Assert.IsTrue(executed);
        }

        [Test]
        public async Task Then_前一步失败_后一步跳过()
        {
            var executed = false;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Then("Step1", (s, c) => Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed("Step1 失败")));
            workflow.Then("Step2", (s, c) =>
            {
                executed = true;
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
            });

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            Assert.IsFalse(executed); // Step2 不应执行
        }

        #endregion

        #region ThenMeasure 测量步骤

        [Test]
        public async Task ThenMeasure_测量成功_流程继续()
        {
            Measurement measurement = null;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.ThenMeasure("Measure1", (s, c) =>
            {
                measurement = Measurement.Succeeded("Voltage", 12.5, "正常", "V");
                return Task.FromResult(measurement);
            });

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(measurement);
            Assert.IsTrue(measurement.Success);
        }

        [Test]
        public async Task ThenMeasure_测量失败_流程短路()
        {
            var step2Executed = false;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.ThenMeasure("Measure1", (s, c) =>
            {
                var m = Measurement.Failed("Voltage", "超压");
                return Task.FromResult(m);
            });
            workflow.Then("Step2", (s, c) =>
            {
                step2Executed = true;
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
            });

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            Assert.IsFalse(step2Executed);
        }

        #endregion

        #region Delay 延迟

        [Test]
        public async Task Delay_指定毫秒延迟()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Delay(100, "Delay100");
            workflow.Then("AfterDelay", (s, c) => Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded()));

            var result = await workflow.GetResultAsync();

            sw.Stop();
            Assert.IsTrue(result.Success);
            Assert.GreaterOrEqual(sw.ElapsedMilliseconds, 80); // 允许一定误差
        }

        #endregion

        #region Retry 重试

        [Test]
        public async Task Retry_首次失败后重试成功()
        {
            var attempts = 0;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Retry("RetryAction", (s, c) =>
            {
                attempts++;
                return Task.FromResult<ExecutionResultBase>(attempts >= 2
                    ? ExecutionResult.Succeeded()
                    : ExecutionResult.Failed($"尝试 {attempts} 失败"));
            }, maxRetries: 3, delayBetweenRetriesMs: 10);

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, attempts);
        }

        [Test]
        public async Task Retry_全部失败_返回最后一次失败()
        {
            var attempts = 0;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Retry("RetryAction", (s, c) =>
            {
                attempts++;
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed($"尝试 {attempts} 失败"));
            }, maxRetries: 2, delayBetweenRetriesMs: 10);

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(2, attempts);
        }

        #endregion

        #region If 条件分支

        [Test]
        public async Task If_条件为真_执行分支()
        {
            var executed = false;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.If(true, w => w.Then("Branch", (s, c) =>
            {
                executed = true;
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
            }));

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
            Assert.IsTrue(executed);
        }

        [Test]
        public async Task If_条件为假_跳过分支()
        {
            var executed = false;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.If(false, w => w.Then("Branch", (s, c) =>
            {
                executed = true;
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
            }));

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
            Assert.IsFalse(executed);
        }

        #endregion

        #region Finally 清理动作

        [Test]
        public async Task Finally_无论成功失败都执行()
        {
            var finallyExecuted = false;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Then("FailStep", (s, c) => Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed("故意失败")));
            workflow.Finally("Cleanup", (s, c) =>
            {
                finallyExecuted = true;
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
            });

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            await workflow.DisposeAsync();
            Assert.IsTrue(finallyExecuted);
        }

        [Test]
        public async Task Finally_LIFO顺序_后进先出()
        {
            var order = new System.Collections.Generic.List<string>();

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Finally("C", (s, c) =>
            {
                order.Add("C");
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
            });
            workflow.Finally("B", (s, c) =>
            {
                order.Add("B");
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
            });
            workflow.Finally("A", (s, c) =>
            {
                order.Add("A");
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
            });

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
            await workflow.DisposeAsync();
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, order);
        }

        #endregion

        #region 流程级超时

        [Test]
        public async Task Start_带流程超时_超时触发失败()
        {
            await using var workflow = MicroWorkflow.Start(_step, _context, workflowTimeoutMs: 100);
            workflow.Then("SlowStep", async (s, c) =>
            {
                await Task.Delay(5000, c.CancellationToken).ConfigureAwait(false);
                return ExecutionResult.Succeeded();
            });

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("取消").Or.Contain("超时"));
        }

        [Test]
        public async Task Start_无超时_慢步骤成功()
        {
            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Then("SlowStep", async (s, c) =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                return ExecutionResult.Succeeded();
            });

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
        }

        #endregion

        #region 异常处理

        [Test]
        public async Task Then_抛异常_流程失败()
        {
            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Then("ThrowStep", (s, c) =>
            {
                throw new InvalidOperationException("测试异常");
            });

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("测试异常"));
        }

        #endregion

        #region 空工作流

        [Test]
        public async Task GetResultAsync_空工作流_返回成功()
        {
            await using var workflow = MicroWorkflow.Start(_step, _context);

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
        }

        #endregion

        #region Parallel 并行分支

        [Test]
        public async Task Parallel_全部成功_流程成功()
        {
            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Parallel("并行分支", new[]
            {
                ("Branch1", (ActionDelegate)((s, c) => Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded()))),
                ("Branch2", (ActionDelegate)((s, c) => Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded())))
            });

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task Parallel_部分失败_流程失败()
        {
            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Parallel("并行分支", new[]
            {
                ("Branch1", (ActionDelegate)((s, c) => Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded()))),
                ("Branch2", (ActionDelegate)((s, c) => Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed("分支2 失败"))))
            });

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("分支2 失败"));
        }

        [Test]
        public async Task Parallel_子分支抛异常_流程失败()
        {
            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Parallel("并行分支", new[]
            {
                ("Branch1", (ActionDelegate)((s, c) => Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded()))),
                ("Branch2", (ActionDelegate)((s, c) => throw new InvalidOperationException("分支2 异常")))
            });

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("分支2 异常"));
        }

        #endregion

        #region ParallelMeasure 并行测量

        [Test]
        public async Task ParallelMeasure_全部成功_测量收集()
        {
            var measurements = new System.Collections.Generic.List<Measurement>();

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.ParallelMeasure("并行测量", new[]
            {
                ("M1", (MeasurementActionDelegate)((s, c) =>
                {
                    var m = Measurement.Succeeded("M1", 1.0);
                    measurements.Add(m);
                    return Task.FromResult(m);
                })),
                ("M2", (MeasurementActionDelegate)((s, c) =>
                {
                    var m = Measurement.Succeeded("M2", 2.0);
                    measurements.Add(m);
                    return Task.FromResult(m);
                }))
            });

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, measurements.Count);
        }

        [Test]
        public async Task ParallelMeasure_部分失败_仅收集成功测量()
        {
            var measurements = new System.Collections.Generic.List<Measurement>();

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.ParallelMeasure("并行测量", new[]
            {
                ("M1", (MeasurementActionDelegate)((s, c) =>
                {
                    var m = Measurement.Succeeded("M1", 1.0);
                    measurements.Add(m);
                    return Task.FromResult(m);
                })),
                ("M2", (MeasurementActionDelegate)((s, c) =>
                {
                    var m = Measurement.Failed("M2", "测量失败");
                    return Task.FromResult(m);
                }))
            });

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, measurements.Count);
            Assert.IsTrue(measurements[0].Success);
        }

        #endregion

        #region ExpectMeasurements 预期测量

        [Test]
        public async Task ExpectMeasurements_无测量_流程失败()
        {
            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.ExpectMeasurements();

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("预期的测量步骤"));
        }

        #endregion

        #region While 循环

        [Test]
        public async Task While_条件为真_执行循环()
        {
            var count = 0;
            var maxIterations = 3;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.While(ctx => count < maxIterations, w =>
            {
                w.Then("LoopStep", (s, c) =>
                {
                    count++;
                    return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
                });
                return w;
            });

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, count);
        }

        [Test]
        public async Task While_体内失败_循环停止()
        {
            var count = 0;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.While(ctx => count < 3, w =>
            {
                w.Then("LoopStep", (s, c) =>
                {
                    count++;
                    if (count >= 2)
                    {
                        return Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed("循环内失败"));
                    }
                    return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
                });
                return w;
            });

            var result = await workflow.GetResultAsync();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(2, count);
        }

        #endregion

        #region Switch 分支

        [Test]
        public async Task Switch_命中Case_执行分支()
        {
            var executed = false;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Switch(ctx => "A", w =>
            {
                w.Case("A", subWf =>
                {
                    executed = true;
                    return subWf;
                });
                w.Default(subWf => subWf);
            });

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
            Assert.IsTrue(executed);
        }

        [Test]
        public async Task Switch_无匹配_执行Default()
        {
            var executed = false;

            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Switch(ctx => "Z", w =>
            {
                w.Case("A", subWf =>
                {
                    executed = true;
                    return subWf;
                });
                w.Default(subWf =>
                {
                    executed = true;
                    return subWf;
                });
            });

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
            Assert.IsTrue(executed);
        }

        [Test]
        public async Task Switch_无匹配无Default_跳过()
        {
            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.Switch(ctx => "Z", w =>
            {
                w.Case("A", subWf => subWf);
            });

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
        }

        #endregion

        #region BestEffort 尽力执行

        [Test]
        public async Task BestEffort_内部失败_流程成功()
        {
            await using var workflow = MicroWorkflow.Start(_step, _context);
            workflow.BestEffort("BestEffortStep", "FakeActionName");

            var result = await workflow.GetResultAsync();

            Assert.IsTrue(result.Success);
        }

        #endregion
    }
}
