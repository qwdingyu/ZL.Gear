using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Devices.Dto;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Services;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Engine.BuiltIn
{
    /// <summary>
    /// MicroWorkflow 综合演示处理器。
    /// 展示 MicroWorkflow 的所有核心功能：
    /// 1. 顺序执行 (Then/ThenMeasure)
    /// 2. 并行执行 (Parallel/ParallelMeasure)
    /// 3. 条件分支 (If/Switch)
    /// 4. 循环结构 (While)
    /// 5. 重试机制 (Retry)
    /// 6. 轮询等待 (WaitUntil)
    /// 7. 清理操作 (Finally)
    /// 8. 延时 (Delay)
    /// 9. 主从协调 (AsMaster/SignalStart)
    /// </summary>
    public class MicroWorkflowDemoHandler : IStepHandler
    {
        private readonly Action<string> _log;

        public MicroWorkflowDemoHandler(Action<string> log = null)
        {
            _log = log ?? (s => { });
        }

        public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            var testMode = context.Get<string>("TestMode") ?? "Comprehensive";
            var enableRetry = context.Get<bool>("EnableRetry");
            var maxRetries = context.Get<int>("MaxRetries", 3);
            var enableParallel = context.Get<bool>("EnableParallel");
            var enableLoop = context.Get<bool>("EnableLoop");
            var loopCount = context.Get<int>("LoopCount", 3);
            var enableConditional = context.Get<bool>("EnableConditionalBranch");
            var modelType = context.Get<string>("ModelType") ?? "ModelA";
            var waitTimeout = context.Get<int>("WaitTimeoutMs", 3000);
            var pollInterval = context.Get<int>("PollIntervalMs", 100);

            _log("========================================");
            _log($"[MicroWorkflow Demo] 开始演示 - 模式: {testMode}");
            _log($"[MicroWorkflow Demo] 配置: Retry={enableRetry}, Parallel={enableParallel}, Loop={enableLoop}");
            _log("========================================");

            try
            {
                await using var workflow = MicroWorkflow.Start(step, context);

                #region 阶段 1: 设备初始化（顺序执行）

                workflow
                    .Then("【阶段1】设备初始化 - 上电", async (s, ctx) =>
                    {
                        _log("→ 执行设备上电...");
                        await Task.Delay(100, ctx.CancellationToken);
                        return ExecutionResult.Succeeded("设备上电成功");
                    })
                    .Delay(200, "等待设备稳定")
                    .Then("【阶段1】设备初始化 - 自检", async (s, ctx) =>
                    {
                        _log("→ 执行设备自检...");
                        await Task.Delay(100, ctx.CancellationToken);
                        return ExecutionResult.Succeeded("自检通过");
                    });

                #endregion

                #region 阶段 2: 条件分支演示

                if (enableConditional)
                {
                    workflow
                        .Then("【阶段2】条件分支 - 模型判断", async (s, ctx) =>
                        {
                            _log($"→ 检测到产品型号: {modelType}");
                            return await ctx.ActionResolver.ResolveAction("MockSwitchAction")(s, ctx);
                        });
                }

                #endregion

                #region 阶段 3: 并行测量演示

                if (enableParallel)
                {
                    workflow
                        .Then("【阶段3】并行测量 - 多通道数据采集", async (s, ctx) =>
                        {
                            _log("→ 开始并行采集电压、电流、温度...");
                            return await ctx.ActionResolver.ResolveAction("MockParallelMeasure")(s, ctx);
                        })
                        .Delay(100, "等待数据处理");
                }

                #endregion

                #region 阶段 4: 重试机制演示

                if (enableRetry)
                {
                    workflow
                        .Then("【阶段4】重试机制 - 网络通信模拟", async (s, ctx) =>
                        {
                            _log("→ 执行需要重试的操作（模拟不稳定网络）...");
                            return await ctx.ActionResolver.ResolveAction("MockRetryAction")(s, ctx);
                        });
                }

                #endregion

                #region 阶段 5: 轮询等待演示

                workflow
                    .Then("【阶段5】轮询等待 - 等待气缸到位", async (s, ctx) =>
                    {
                        _log($"→ 开始轮询等待，最多 {waitTimeout}ms...");
                        return await ctx.ActionResolver.ResolveAction("MockWaitUntil")(s, ctx);
                    });

                #endregion

                #region 阶段 6: 循环结构演示

                if (enableLoop)
                {
                    workflow
                        .Then("【阶段6】循环结构 - 连续测量3次", async (s, ctx) =>
                        {
                            _log("→ 执行循环测量...");
                            return await ctx.ActionResolver.ResolveAction("MockWhileLoop")(s, ctx);
                        });
                }

                #endregion

                #region 阶段 7: 清理操作

                workflow
                    .Finally("【阶段7】清理操作 - 设备复位", async (s, ctx) =>
                    {
                        _log("→ 执行设备复位...");
                        await Task.Delay(100, ctx.CancellationToken);
                        return ExecutionResult.Succeeded("设备复位完成");
                    })
                    .Finally("【阶段7】清理操作 - 生成报告", async (s, ctx) =>
                    {
                        _log("→ 生成测试报告...");
                        await Task.Delay(50, ctx.CancellationToken);
                        return ExecutionResult.Succeeded("报告生成完成");
                    });

                #endregion

                #region 阶段 8: 结果判定

                workflow
                    .ExpectMeasurements()
                    .Then("【阶段8】结果判定 - 数据分析", async (s, ctx) =>
                    {
                        _log("→ 分析测量数据...");
                        return ExecutionResult.Succeeded("数据符合规格");
                    });

                #endregion

                var result = await workflow.GetResultAsync();

                _log("========================================");
                _log($"[MicroWorkflow Demo] 执行完成 - 结果: {(result.Success ? "成功" : "失败")}");
                if (!result.Success)
                {
                    _log($"[MicroWorkflow Demo] 失败原因: {result.Message}");
                }
                _log("========================================");

                return result;
            }
            catch (Exception ex)
            {
                _log($"[MicroWorkflow Demo] 执行异常: {ex.Message}");
                return ExecutionResult.Failed($"演示执行异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Mock 动作提供者，用于演示 MicroWorkflow 功能
    /// </summary>
    public static class MicroWorkflowDemoActions
    {
        private static int _retryCount = 0;
        private static int _loopCount = 0;
        private static int _waitCount = 0;
        private static Action<string> _log;

        public static void Register(IActionRegistry registry, Action<string> log)
        {
            _log = log;

            registry.RegisterAction("MockSwitchAction", async (step, ctx) =>
            {
                var modelType = ctx.Get<string>("ModelType") ?? "ModelA";

                await using var workflow = MicroWorkflow.Start(step, ctx);

                workflow
                    .Switch<string>(c => modelType, builder =>
                    {
                        builder.Case("ModelA", wf => wf
                            .Then("ModelA 分支 - 电压测试", async (s, c) =>
                            {
                                _log("[Switch] 执行 ModelA 电压测试");
                                var m = Measurement<double>.Create("voltage", 12.0 + new Random().NextDouble(), true, "ModelA 电压测试", "V", 1);
                                c.Variables.Set("voltage", m.Value);
                                return await Task.FromResult(ExecutionResult.Succeeded());
                            })
                        );
                        builder.Case("ModelB", wf => wf
                            .Then("ModelB 分支 - 电流测试", async (s, c) =>
                            {
                                _log("[Switch] 执行 ModelB 电流测试");
                                var m = Measurement<double>.Create("current", 2.5 + new Random().NextDouble(), true, "ModelB 电流测试", "A", 1);
                                c.Variables.Set("current", m.Value);
                                return await Task.FromResult(ExecutionResult.Succeeded());
                            })
                        );
                        builder.Default(wf => wf
                            .Then("默认分支 - 跳过测试", async (s, c) =>
                            {
                                _log("[Switch] 执行默认分支 - 跳过");
                                return await Task.FromResult(ExecutionResult.Succeeded("跳过测试"));
                            })
                        );
                    });

                return await workflow.GetResultAsync();
            });

            registry.RegisterAction("MockParallelMeasure", async (step, ctx) =>
            {
                await using var workflow = MicroWorkflow.Start(step, ctx);

                workflow
                    .ParallelMeasure("同时采集电压、电流、温度",
                        ("电压采集", async (s, c) =>
                        {
                            await Task.Delay(50, c.CancellationToken);
                            return Measurement<double>.Create("voltage", 12.0 + new Random().NextDouble() * 0.1, true, "电压测量", "V", 1);
                        }),
                        ("电流采集", async (s, c) =>
                        {
                            await Task.Delay(50, c.CancellationToken);
                            return Measurement<double>.Create("current", 1.5 + new Random().NextDouble() * 0.1, true, "电流测量", "A", 1);
                        }),
                        ("温度采集", async (s, c) =>
                        {
                            await Task.Delay(50, c.CancellationToken);
                            return Measurement<double>.Create("temperature", 25.0 + new Random().NextDouble(), true, "温度测量", "℃", 1);
                        })
                    );

                return await workflow.GetResultAsync();
            });

            registry.RegisterAction("MockRetryAction", async (step, ctx) =>
            {
                var maxRetries = ctx.Get<int>("MaxRetries", 3);
                var retryDelay = ctx.Get<int>("RetryDelayMs", 100);
                _retryCount = 0;

                await using var workflow = MicroWorkflow.Start(step, ctx);

                workflow
                    .Retry("模拟不稳定操作（网络通信）", async (s, c) =>
                    {
                        _retryCount++;
                        var random = new Random().NextDouble();
                        _log($"[Retry] 第 {_retryCount} 次尝试");
                        if (random < 0.6)
                        {
                            return ExecutionResult.Failed($"模拟网络超时 ({_retryCount}/{maxRetries})");
                        }
                        return ExecutionResult.Succeeded("通信成功");
                    }, maxRetries, retryDelay);

                return await workflow.GetResultAsync();
            });

            registry.RegisterAction("MockWaitUntil", async (step, ctx) =>
            {
                var timeout = ctx.Get<int>("WaitTimeoutMs", 3000);
                var pollInterval = ctx.Get<int>("PollIntervalMs", 100);
                _waitCount = 0;

                await using var workflow = MicroWorkflow.Start(step, ctx);

                workflow
                    .WaitUntil("等待气缸到位信号", async (s, c) =>
                    {
                        _waitCount++;
                        if (_waitCount >= 4)
                        {
                            _log($"[WaitUntil] 第 {_waitCount} 次检查：气缸到位");
                            return true;
                        }
                        _log($"[WaitUntil] 第 {_waitCount} 次检查：气缸未到位，继续等待...");
                        await Task.Delay(pollInterval, c.CancellationToken);
                        return false;
                    }, timeout, pollInterval);

                return await workflow.GetResultAsync();
            });

            registry.RegisterAction("MockWhileLoop", async (step, ctx) =>
            {
                var loopCount = ctx.Get<int>("LoopCount", 3);
                _loopCount = 0;

                await using var workflow = MicroWorkflow.Start(step, ctx);

                workflow
                    .While(c => _loopCount < loopCount, wf => wf
                        .Then("循环测量", async (s, c) =>
                        {
                            _loopCount++;
                            _log($"[Loop] 执行第 {_loopCount} 次测量");
                            var m = Measurement<double>.Create($"loop_measure_{_loopCount}", 10.0 * _loopCount, true, $"第 {_loopCount} 次测量", "units", 1);
                            c.Variables.Set($"loop_result_{_loopCount}", m.Value);
                            return await Task.FromResult(ExecutionResult.Succeeded());
                        })
                        .Delay(50, "测量间隔")
                    )
                    .Then("循环完成", async (s, c) =>
                    {
                        _log($"[Loop] 完成 {_loopCount} 次循环");
                        return await Task.FromResult(ExecutionResult.Succeeded($"循环完成，共 {_loopCount} 次"));
                    });

                return await workflow.GetResultAsync();
            });
        }
    }
}
