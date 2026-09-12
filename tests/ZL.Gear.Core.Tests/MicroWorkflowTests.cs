using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Core.Tests
{
    [TestFixture]
    public class MicroWorkflowTests
    {
        private StepConfig _step;
        private StepContext _context;

        private class ServiceProviderStub : IServiceProvider
        {
            public object GetService(Type serviceType) => null;
        }

        [SetUp]
        public void Setup()
        {
            _step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "TestWorkflow",
                Parameters = new System.Collections.Generic.Dictionary<string, object>()
            };

            var variables = new ContextVariableStore();
            _context = new StepContext(
                stepKey: _step.StepKey,
                stepCfg: _step,
                activeDevices: new Dictionary<string, IDevice>(),
                serviceProvider: new ServiceProviderStub(),
                token: CancellationToken.None,
                runTestMode: RunTestMode.Auto,
                variables: variables
            );
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
                measurement = Measurement.Succeeded("Voltage", 12.5, "V", "正常");
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
    }
}
