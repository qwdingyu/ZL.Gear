using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Newtonsoft.Json.Linq;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine;
using ZL.Gear.Testing.Common;

namespace ZL.Gear.Engine.Tests
{
    [TestFixture]
    public class DynamicFlowHandlerTests
    {
        private DynamicFlowHandler _handler;
        private Mock<IActionResolver> _mockResolver;
        private StepContext _context;

        [SetUp]
        public void Setup()
        {
            _mockResolver = new Mock<IActionResolver>();
            _handler = new DynamicFlowHandler();

            _mockResolver.Setup(r => r.ResolveAction(It.IsAny<string>()))
                .Returns<string>(key => (s, c) => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult.Succeeded($"[Action] {key}")));

            _mockResolver.Setup(r => r.ResolveMeasurement(It.IsAny<string>()))
                .Returns<string>(key => (s, c) => Task.FromResult(Measurement.Succeeded(key, 42.0)));

            _context = StepContextFactory.CreateWithActionResolver(_mockResolver.Object);
        }

        #region ExecuteAsync - JSON 解析失败

        [Test]
        public async Task ExecuteAsync_缺少WorkflowDefinition_返回失败()
        {
            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>()
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("缺少 WorkflowDefinition"));
        }

        [Test]
        public async Task ExecuteAsync_非法JSON_返回失败()
        {
            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", "{ invalid json" }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("解析失败"));
        }

        #endregion

        #region ExecuteAsync - 正常流程

        [Test]
        public async Task ExecuteAsync_简单Sequence_全部成功()
        {
            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "node1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "Setup",
                        ["Description"] = "设置"
                    },
                    new JObject
                    {
                        ["Id"] = "node2",
                        ["Type"] = "Delay",
                        ["DelayMs"] = 10,
                        ["Description"] = "等待"
                    },
                    new JObject
                    {
                        ["Id"] = "node3",
                        ["Type"] = "Action",
                        ["ActionKey"] = "Cleanup",
                        ["Description"] = "清理"
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task ExecuteAsync_包含Measure_成功()
        {
            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "m1",
                        ["Type"] = "Measure",
                        ["ActionKey"] = "ReadVoltage",
                        ["Description"] = "读电压"
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsTrue(result.Success);
        }

        #endregion

        #region Condition 守卫

        [Test]
        public async Task ExecuteAsync_Condition为假_跳过节点()
        {
            // 在上下文中设置条件变量
            _context.Variables.SetShared("RunBranch", false);

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "node1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "ShouldSkip",
                        ["Condition"] = "RunBranch == true",
                        ["Description"] = "条件分支"
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsTrue(result.Success);
            // 验证条件为假时动作被跳过
            _mockResolver.Verify(r => r.ResolveAction("ShouldSkip"), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_Condition为真_执行节点()
        {
            _context.Variables.SetShared("RunBranch", true);

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "node1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "ShouldRun",
                        ["Condition"] = "RunBranch == true",
                        ["Description"] = "条件分支"
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsTrue(result.Success);
            _mockResolver.Verify(r => r.ResolveAction("ShouldRun"), Times.Once);
        }

        #endregion

        #region Finalizers 清理动作

        [Test]
        public async Task ExecuteAsync_Finalizers_无论成功失败都执行()
        {
            _mockResolver.Setup(r => r.ResolveAction("FailAction"))
                .Returns<string>(_ => (s, c) => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult.Failed("故意失败")));

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "node1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "FailAction",
                        ["Description"] = "失败动作"
                    }
                },
                ["Finalizers"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "fin1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "Cleanup",
                        ["Description"] = "清理"
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsFalse(result.Success);
            _mockResolver.Verify(r => r.ResolveAction("Cleanup"), Times.Once);
        }

        #endregion

        #region Parallel 非法子节点检测

        [Test]
        public async Task ExecuteAsync_Parallel包含Measure_返回失败()
        {
            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "p1",
                        ["Type"] = "Parallel",
                        ["Description"] = "非法并行",
                        ["Children"] = new JArray
                        {
                            new JObject
                            {
                                ["Id"] = "m1",
                                ["Type"] = "Measure",
                                ["ActionKey"] = "Read",
                                ["Description"] = "测量"
                            }
                        }
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("Parallel 不能包含 Measure"));
        }

        #endregion

        #region 流程级超时

        [Test]
        public async Task ExecuteAsync_流程超时_返回失败()
        {
            _mockResolver.Setup(r => r.ResolveAction("SlowAction"))
                .Returns<string>(_ => (s, c) => SlowActionAsync(c));

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "node1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "SlowAction",
                        ["Description"] = "慢动作"
                    }
                },
                ["WorkflowTimeoutMs"] = 100
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("取消").Or.Contain("超时"));
        }

        #endregion

        #region 失败路径与边界（补充）

        [Test]
        public async Task ExecuteAsync_Delay被取消_返回失败()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "d1",
                        ["Type"] = "Delay",
                        ["DelayMs"] = 1000,
                        ["Description"] = "等待"
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var childContext = _context.WithToken(cts.Token);
            var result = await _handler.ExecuteAsync(step, childContext);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("取消"));
        }

        [Test]
        public async Task ExecuteAsync_中间Action失败_后续节点短路()
        {
            var callCount = 0;
            _mockResolver.Setup(r => r.ResolveAction("FailAction"))
                .Returns<string>(_ => (s, c) =>
                {
                    callCount++;
                    return Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed("故意失败"));
                });
            _mockResolver.Setup(r => r.ResolveAction("AfterFail"))
                .Returns<string>(_ => (s, c) =>
                {
                    callCount++;
                    return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
                });

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "node1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "FailAction",
                        ["Description"] = "失败动作"
                    },
                    new JObject
                    {
                        ["Id"] = "node2",
                        ["Type"] = "Action",
                        ["ActionKey"] = "AfterFail",
                        ["Description"] = "后续动作"
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public async Task ExecuteAsync_Measure失败_不记录测量()
        {
            _mockResolver.Setup(r => r.ResolveMeasurement("BadMeasure"))
                .Returns<string>(_ => (s, c) => Task.FromResult<Measurement<string>.Failed("BadMeasure", "超压")));

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "m1",
                        ["Type"] = "Measure",
                        ["ActionKey"] = "BadMeasure",
                        ["Description"] = "坏测量"
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("超压"));
        }

        [Test]
        public async Task ExecuteAsync_Condition非法表达式_跳过节点()
        {
            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "node1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "ShouldSkip",
                        ["Condition"] = "NonExist == true",
                        ["Description"] = "条件分支"
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsTrue(result.Success);
            _mockResolver.Verify(r => r.ResolveAction("ShouldSkip"), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_WaitUntil超时_返回失败()
        {
            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "w1",
                        ["Type"] = "WaitUntil",
                        ["Condition"] = "NeverReady == true",
                        ["Description"] = "等待",
                        ["TimeoutMs"] = 50,
                        ["IntervalMs"] = 10
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("等待超时"));
        }

        #endregion

        #region 异常处理

        [Test]
        public async Task ExecuteAsync_动作抛异常_流程失败()
        {
            _mockResolver.Setup(r => r.ResolveAction("ThrowAction"))
                .Returns((string key) => (s, c) => throw new InvalidOperationException("测试异常"));

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "node1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "ThrowAction",
                        ["Description"] = "抛异常"
                    }
                }
            };

            var step = new StepConfig
            {
                StepKey = "WF-001",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    { "WorkflowDefinition", flowJson }
                }
            };

            var result = await _handler.ExecuteAsync(step, _context);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("测试异常"));
        }

        #endregion

        private static async Task<ExecutionResultBase> SlowActionAsync(StepContext context)
        {
            await Task.Delay(5000, context.CancellationToken).ConfigureAwait(false);
            return ExecutionResult.Succeeded();
        }
    }
}
