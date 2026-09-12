using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
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
                .Returns<string>(_ => (s, c) => Task.FromResult(Measurement.Failed("BadMeasure", "超压")));

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

        #region 补充场景

        [Test]
        public async Task ExecuteAsync_空Sequence_直接成功()
        {
            var flowJson = new JObject
            {
                ["Sequence"] = new JArray()
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
        public async Task ExecuteAsync_Retry失败后重试_最终成功()
        {
            var callCount = 0;
            _mockResolver.Setup(r => r.ResolveAction("FlakyAction"))
                .Returns<string>(_ => (s, c) =>
                {
                    callCount++;
                    return callCount < 3
                        ? Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed("瞬时故障"))
                        : Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
                });

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "r1",
                        ["Type"] = "Retry",
                        ["ActionKey"] = "FlakyAction",
                        ["Description"] = "重试",
                        ["RetryCount"] = 3,
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

            Assert.IsTrue(result.Success);
            Assert.GreaterOrEqual(callCount, 3);
        }

        [Test]
        public async Task ExecuteAsync_Retry耗尽_返回失败()
        {
            _mockResolver.Setup(r => r.ResolveAction("AlwaysFail"))
                .Returns<string>(_ => (s, c) => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult.Failed("永久失败")));

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "r1",
                        ["Type"] = "Retry",
                        ["ActionKey"] = "AlwaysFail",
                        ["Description"] = "重试",
                        ["RetryCount"] = 2,
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
            Assert.That(result.Message, Does.Contain("永久失败"));
        }

        [Test]
        public async Task ExecuteAsync_Parallel全部成功_整体成功()
        {
            // 纯动作（非测量），避免被 ResolveMeasurement 双注册拦截
            _mockResolver.Setup(r => r.ResolveAction("BranchA"))
                .Returns<string>(_ => (s, c) => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult.Succeeded("[BranchA]")));
            _mockResolver.Setup(r => r.ResolveAction("BranchB"))
                .Returns<string>(_ => (s, c) => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult.Succeeded("[BranchB]")));
            _mockResolver.Setup(r => r.ResolveMeasurement("BranchA"))
                .Returns<string>(_ => null);
            _mockResolver.Setup(r => r.ResolveMeasurement("BranchB"))
                .Returns<string>(_ => null);

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "p1",
                        ["Type"] = "Parallel",
                        ["Description"] = "并行支路",
                        ["Children"] = new JArray
                        {
                            new JObject
                            {
                                ["Id"] = "a1",
                                ["Type"] = "Action",
                                ["ActionKey"] = "BranchA",
                                ["Description"] = "支路 A"
                            },
                            new JObject
                            {
                                ["Id"] = "b1",
                                ["Type"] = "Action",
                                ["ActionKey"] = "BranchB",
                                ["Description"] = "支路 B"
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

            Assert.IsTrue(result.Success);
            _mockResolver.Verify(r => r.ResolveAction("BranchA"), Times.Once);
            _mockResolver.Verify(r => r.ResolveAction("BranchB"), Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_Parallel一子节点失败_整体失败()
        {
            var callCount = 0;
            // 纯动作（非测量）
            _mockResolver.Setup(r => r.ResolveAction("FailBranch"))
                .Returns<string>(_ => (s, c) =>
                {
                    callCount++;
                    return Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed("[FailBranch]"));
                });
            _mockResolver.Setup(r => r.ResolveAction("OkBranch"))
                .Returns<string>(_ => (s, c) => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult.Succeeded("[OkBranch]")));
            _mockResolver.Setup(r => r.ResolveMeasurement("FailBranch"))
                .Returns<string>(_ => null);
            _mockResolver.Setup(r => r.ResolveMeasurement("OkBranch"))
                .Returns<string>(_ => null);

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "p1",
                        ["Type"] = "Parallel",
                        ["Description"] = "并行失败",
                        ["Children"] = new JArray
                        {
                            new JObject
                            {
                                ["Id"] = "f1",
                                ["Type"] = "Action",
                                ["ActionKey"] = "FailBranch",
                                ["Description"] = "失败支路"
                            },
                            new JObject
                            {
                                ["Id"] = "o1",
                                ["Type"] = "Action",
                                ["ActionKey"] = "OkBranch",
                                ["Description"] = "正常支路"
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
            _mockResolver.Verify(r => r.ResolveAction("FailBranch"), Times.Once);
            _mockResolver.Verify(r => r.ResolveAction("OkBranch"), Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_WaitUntil条件为真_立即成功()
        {
            _context.Variables.SetShared("Ready", true);

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "w1",
                        ["Type"] = "WaitUntil",
                        ["Description"] = "等待就绪",
                        ["Condition"] = "Ready == true",
                        ["TimeoutMs"] = 1000,
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

            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task ExecuteAsync_Finalizers失败_流程仍标记失败()
        {
            _mockResolver.Setup(r => r.ResolveAction("FailAction"))
                .Returns<string>(_ => (s, c) => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult.Failed("主流程失败")));
            _mockResolver.Setup(r => r.ResolveAction("BadFinalizer"))
                .Returns<string>(_ => (s, c) => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult.Failed("清理失败")));

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
                        ["ActionKey"] = "BadFinalizer",
                        ["Description"] = "失败清理"
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
            _mockResolver.Verify(r => r.ResolveAction("FailAction"), Times.Once);
            _mockResolver.Verify(r => r.ResolveAction("BadFinalizer"), Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_Calculate节点_成功()
        {
            _mockResolver.Setup(r => r.ResolveAction("Calculate"))
                .Returns<string>(_ => (s, c) => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult.Succeeded("[Calculate] Margin=2.5")));

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "c1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "Calculate",
                        ["Description"] = "计算裕量",
                        ["Args"] = new JObject
                        {
                            ["Expression"] = "LimitOhm - MeasuredOhm",
                            ["OutputKey"] = "Margin"
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

            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task ExecuteAsync_Assert节点_通过()
        {
            _mockResolver.Setup(r => r.ResolveAction("Assert"))
                .Returns<string>(_ => (s, c) => Task.FromResult<ExecutionResultBase>(
                    ExecutionResult.Succeeded("[Assert]")));

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "a1",
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Description"] = "L1 检查",
                        ["Args"] = new JObject
                        {
                            ["Check"] = "Margin > 0",
                            ["Message"] = "裕量不足"
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

            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task ExecuteAsync_Group节点_等价Sequence()
        {
            var callCount = 0;
            _mockResolver.Setup(r => r.ResolveAction("GroupAction"))
                .Returns<string>(_ => (s, c) =>
                {
                    callCount++;
                    return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded("[Group]"));
                });

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "g1",
                        ["Type"] = "Group",
                        ["Description"] = "视觉分组",
                        ["Children"] = new JArray
                        {
                            new JObject
                            {
                                ["Id"] = "n1",
                                ["Type"] = "Action",
                                ["ActionKey"] = "GroupAction",
                                ["Description"] = "分组内动作"
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

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public async Task ExecuteAsync_Retry节点_被取消_返回失败()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var callCount = 0;
            _mockResolver.Setup(r => r.ResolveAction("RetryAction"))
                .Returns<string>(_ => (s, c) =>
                {
                    callCount++;
                    return Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed("瞬时故障"));
                });

            var flowJson = new JObject
            {
                ["Sequence"] = new JArray
                {
                    new JObject
                    {
                        ["Id"] = "r1",
                        ["Type"] = "Retry",
                        ["ActionKey"] = "RetryAction",
                        ["Description"] = "取消重试",
                        ["RetryCount"] = 3,
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

            var childContext = _context.WithToken(cts.Token);
            var result = await _handler.ExecuteAsync(step, childContext);

            Assert.IsFalse(result.Success);
            Assert.That(result.Message, Does.Contain("取消"));
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
