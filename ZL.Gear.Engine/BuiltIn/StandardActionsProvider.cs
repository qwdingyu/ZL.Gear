using System;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// 基础设施属性：System.Delay, Log.Info, Assert 等不属于任何特定的行业业务（如汽车、电池），它们是工作流引擎运行时的元语（Primitives）。
    /// 就像 C# 语言本身内置了 Thread.Sleep 一样，Engine 作为执行引擎，理应内置这些基础能力。
    /// </summary>
    public class StandardActionsProvider : IWorkflowActionProvider
    {
        public void RegisterActions(IActionRegistry registry)
        {
            // 注册系统级通用动作
            registry.RegisterAction("System.Delay", DelayLogic);

            registry.RegisterAction("Log.Info", async (step, ctx) =>
            {
                ctx.Log($"[Info] {step.Description}");
                return ExecutionResult.Succeeded();
            });

            // 可以在这里注册一些非常高频使用的短名，作为 Alias; 但要注意冲突风险
            registry.RegisterAction("Delay", DelayLogic, RegistrationPolicy.Ignore);

            // --- 设备通用操作元语 (Primitives for Device Interaction) ---
            registry.RegisterAction("Write", async (step, ctx) =>
            {
                var targetId = step.Target;
                if (string.IsNullOrEmpty(targetId)) return ExecutionResult.Failed("Write 动作缺少 Target 设备");

                var device = ctx.GetDevice<IDevice>(targetId);
                var commandName = ctx.Get<string>("Command") ?? "Write";
                var result = await device.ExecuteAsync(commandName, step.Parameters, ctx);
                return result.Success ? ExecutionResult.Succeeded(result.Message) : ExecutionResult.Failed(result.Message);
            });

            registry.RegisterAction("Read", async (step, ctx) =>
            {
                var targetId = step.Target;
                if (string.IsNullOrEmpty(targetId)) return ExecutionResult.Failed("Read 动作缺少 Target 设备");

                var device = ctx.GetDevice<IDevice>(targetId);
                var commandName = ctx.Get<string>("Command") ?? "Read";
                var result = await device.ExecuteAsync(commandName, step.Parameters, ctx);
                return result.Success ? ExecutionResult<object>.Succeeded(result.Value, result.SamplesCollected, result.Message) : ExecutionResult.Failed(result.Message);
            });

            registry.RegisterAction("Query", async (step, ctx) =>
            {
                var targetId = step.Target;
                if (string.IsNullOrEmpty(targetId)) return ExecutionResult.Failed("Query 动作缺少 Target 设备");

                var device = ctx.GetDevice<IDevice>(targetId);
                var commandName = ctx.Get<string>("Command") ?? "Query";

                var result = await device.ExecuteAsync(commandName, step.Parameters, ctx);
                return result.Success ? ExecutionResult<object>.Succeeded(result.Value, result.SamplesCollected, result.Message) : ExecutionResult.Failed(result.Message);
            });

            registry.RegisterMeasurement("Query", async (step, ctx) =>
            {
                var targetId = step.Target;
                if (string.IsNullOrEmpty(targetId)) return Measurement.Create(step.MeasurementKey, null, false, "Query 测量缺少 Target 设备");

                var device = ctx.GetDevice<IDevice>(targetId);
                var commandName = ctx.Get<string>("Command") ?? "Query";

                var result = await device.ExecuteAsync(commandName, step.Parameters, ctx);
                return Measurement.Create(step.MeasurementKey, result.Value, result.Success, result.Message);
            });

            registry.RegisterMeasurement("Read", async (step, ctx) =>
            {
                var targetId = step.Target;
                if (string.IsNullOrEmpty(targetId)) return Measurement.Create(step.MeasurementKey, null, false, "Read 测量缺少 Target 设备");

                var device = ctx.GetDevice<IDevice>(targetId);
                var commandName = ctx.Get<string>("Command") ?? "Read";
                var result = await device.ExecuteAsync(commandName, step.Parameters, ctx);
                return Measurement.Create(step.MeasurementKey, result.Value, result.Success, result.Message);
            });

            registry.RegisterAction("SetVariable", async (step, ctx) =>
            {
                var key = ctx.Get<string>("Key");
                var val = ctx.Get<object>("Value");
                if (!string.IsNullOrEmpty(key))
                {
                    // DynamicFlow 节点在子作用域执行；必须写到父级流程变量，后继节点才能读到
                    ctx.Variables.SetShared(key, val);
                    return ExecutionResult.Succeeded($"已将变量 '{key}' 设置为 '{val}'");
                }
                return ExecutionResult.Failed("SetVariable 缺少 'Key' 参数");
            });

            registry.RegisterAction("Log", async (step, ctx) =>
            {
                var msg = ctx.Get<string>("Message") ?? step.Description;
                ctx.Log($"[Log] {msg}");
                return ExecutionResult.Succeeded();
            });

            registry.RegisterAction("Assert", async (step, ctx) =>
            {
                // Condition 可能是：bool（Args 已用 @ 表达式求值）、或字符串表达式（行业 JSON 常见写法）
                // 禁止对无法转 bool 的字符串静默 default(true) —— 会造成误 PASS
                bool ok = true;
                if (step.Parameters != null && step.Parameters.TryGetValue("Condition", out var raw) && raw != null)
                {
                    if (raw is bool b)
                    {
                        ok = b;
                    }
                    else if (raw is string expr)
                    {
                        ok = ctx.Evaluate(expr);
                    }
                    else
                    {
                        try
                        {
                            ok = Convert.ToBoolean(raw);
                        }
                        catch
                        {
                            ok = ctx.Evaluate(Convert.ToString(raw));
                        }
                    }
                }

                if (!ok)
                {
                    var msg = ctx.Get<string>("Message") ?? "断言失败";
                    return ExecutionResult.Failed(msg);
                }
                return ExecutionResult.Succeeded();
            });

            registry.RegisterAction("Calculate", async (step, ctx) =>
            {
                var expression = ctx.Get<string>("Expression");
                var outputKey = ctx.Get<string>("OutputKey") ?? "CalcResult";

                if (string.IsNullOrEmpty(expression)) return ExecutionResult.Failed("Calculate 缺少 'Expression' 参数");

                try
                {
                    // 必须走 EvaluateExpression：EvaluateValue 失败会静默退回公式字符串，导致后续 Assert 误判
                    var result = ctx.Evaluator.EvaluateExpression(expression, ctx.Variables.AsDictionary());
                    ctx.Variables.SetShared(outputKey, result);
                    return ExecutionResult.Succeeded($"计算完成: {expression} = {result}");
                }
                catch (Exception ex)
                {
                    return ExecutionResult.Failed($"计算失败: {ex.Message}");
                }
            });

            // --- 主从同步动作 ---
            registry.RegisterAction("Sync.SignalStart", async (step, ctx) =>
            {
                if (ctx.Variables.TryGetSignalPair(step.StepKey, out var signals))
                {
                    ctx.Log($"[信令] 通知从属步骤开始...");
                    signals.StartSignal.TrySetResult(true);
                    return ExecutionResult.Succeeded();
                }
                return ExecutionResult.Failed("无法找到信令对");
            });

            registry.RegisterAction("Sync.StopMaster", async (step, ctx) =>
            {
                if (ctx.Variables.TryGetSignalPair(step.StepKey, out var signals))
                {
                    ctx.Log($"[信令] 通知所有从属步骤停止...");
                    signals.EndSignalCts?.Cancel();
                    return ExecutionResult.Succeeded();
                }
                return ExecutionResult.Failed("无法找到信令对");
            });
        }

        private static async Task<ExecutionResultBase> DelayLogic(StepConfig step, StepContext ctx)
        {
            // 优先使用 TimeoutMs，如果没有设置则默认 1000ms
            int delayMs = step.TimeoutMs > 0 ? step.TimeoutMs : 1000;

            // 如果你想更灵活，支持从 Parameters 读取动态延时
            if (step.Parameters != null)
            {
                if (step.Parameters.TryGetValue("Duration", out var val) || 
                    step.Parameters.TryGetValue("DelayMs", out val) ||
                    step.Parameters.TryGetValue("delayMs", out val) ||
                    step.Parameters.TryGetValue("Delay", out val))
                {
                    if (int.TryParse(val?.ToString(), out int d)) delayMs = d;
                }
            }

            ctx.Log($"-> 系统延时: {delayMs}ms");
            await Task.Delay(delayMs, ctx.CancellationToken);
            return ExecutionResult.Succeeded();
        }
    }
}
