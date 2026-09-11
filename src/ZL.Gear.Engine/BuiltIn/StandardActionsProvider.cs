using System;
using System.Globalization;
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
                // docs/133：L1 Check → L0；L0 Left/Op/Right|RightVar；L2 Condition 逃逸。禁止误 PASS。
                object condRaw = null;
                object checkRaw = null;
                var p = step.Parameters;
                var hasLeft = p != null && p.ContainsKey("Left");
                var hasOp = p != null && p.ContainsKey("Op");
                var hasCheck = p != null
                    && p.TryGetValue("Check", out checkRaw)
                    && checkRaw != null
                    && !(checkRaw is string ch && string.IsNullOrWhiteSpace(ch));
                var hasCondition = p != null
                    && p.TryGetValue("Condition", out condRaw)
                    && condRaw != null
                    && !(condRaw is string cs && string.IsNullOrWhiteSpace(cs));

                if (hasCheck)
                {
                    if (hasLeft || hasOp || hasCondition)
                        return ExecutionResult.Failed("L1 Check 禁止与 Left/Op/Condition 同时出现。");

                    if (!AssertCheckParser.TryParse(Convert.ToString(checkRaw), out var d, out var perr))
                        return ExecutionResult.Failed("Check 解析失败: " + perr);

                    var message = ctx.Get<string>("Message") ?? "断言失败";
                    return EvaluateAssertL0(ctx, d.Left, d.Op, d.HasRightVar, d.RightVar, d.RightLiteral, message);
                }

                if (hasLeft && hasOp)
                {
                    if (hasCondition)
                        return ExecutionResult.Failed("结构化 Assert 禁止与 Condition 同时出现。");

                    var leftName = ctx.Get<string>("Left");
                    var opRaw = ctx.Get<string>("Op");
                    if (!AssertCheckParser.TryNormalizeOp(opRaw, out var op))
                        return ExecutionResult.Failed("未知 Op: " + opRaw);

                    var message = ctx.Get<string>("Message") ?? "断言失败";
                    var hasRight = p.ContainsKey("Right");
                    var hasRightVar = p.ContainsKey("RightVar");
                    if (hasRight == hasRightVar)
                        return ExecutionResult.Failed("结构化 Assert 须且仅指定 Right 或 RightVar 之一。");

                    string rightVar = hasRightVar ? ctx.Get<string>("RightVar") : null;
                    object rightLit = hasRight ? ctx.Get<object>("Right") : null;
                    return EvaluateAssertL0(ctx, leftName, op, hasRightVar, rightVar, rightLit, message);
                }

                if (hasLeft || hasOp)
                    return ExecutionResult.Failed("结构化 Assert 需要同时提供 Left 与 Op（或改用 Check）。");

                if (!hasCondition)
                    return ExecutionResult.Failed("Assert 缺少判定：请提供 Check，或 Left+Op+Right|RightVar，或 Condition。");

                bool ok;
                if (condRaw is bool b) ok = b;
                else if (condRaw is string expr) ok = ctx.Evaluate(expr);
                else
                {
                    try { ok = Convert.ToBoolean(condRaw); }
                    catch { ok = ctx.Evaluate(Convert.ToString(condRaw)); }
                }

                if (!ok)
                    return ExecutionResult.Failed(ctx.Get<string>("Message") ?? "断言失败");
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

        private static ExecutionResultBase EvaluateAssertL0(
            StepContext ctx,
            string leftName,
            string op,
            bool hasRightVar,
            string rightVar,
            object rightLiteral,
            string message)
        {
            if (string.IsNullOrWhiteSpace(leftName) || string.IsNullOrWhiteSpace(op))
                return ExecutionResult.Failed("结构化 Assert 需要 Left 与 Op。");

            if (!ctx.Variables.TryGet<object>(leftName, out var leftVal) || leftVal == null)
                return ExecutionResult.Failed($"结构化 Assert 缺少变量: {leftName}");

            object rightVal;
            if (hasRightVar)
            {
                if (string.IsNullOrWhiteSpace(rightVar)
                    || !ctx.Variables.TryGet<object>(rightVar, out rightVal)
                    || rightVal == null)
                    return ExecutionResult.Failed($"结构化 Assert 缺少变量: {rightVar}");
            }
            else
            {
                rightVal = rightLiteral;
            }

            if (!TryCompareAssert(op, leftVal, rightVal, out var pass, out var err))
                return ExecutionResult.Failed(err ?? message);

            return pass ? ExecutionResult.Succeeded() : ExecutionResult.Failed(message);
        }

        private static bool TryCompareAssert(string op, object left, object right, out bool pass, out string error)
        {
            pass = false;
            error = null;
            if (!AssertCheckParser.TryNormalizeOp(op, out var opNorm))
            {
                error = "未知 Op: " + op;
                return false;
            }

            if (left is bool lb && right is bool rb && (opNorm == "Eq" || opNorm == "Neq"))
            {
                pass = opNorm == "Eq" ? lb == rb : lb != rb;
                return true;
            }

            if (left is string ls && right is string rs && (opNorm == "Eq" || opNorm == "Neq"))
            {
                pass = opNorm == "Eq"
                    ? string.Equals(ls, rs, StringComparison.Ordinal)
                    : !string.Equals(ls, rs, StringComparison.Ordinal);
                return true;
            }

            double ld, rd;
            try
            {
                ld = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                rd = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                error = "结构化 Assert 无法转为数值: " + ex.Message;
                return false;
            }

            switch (opNorm)
            {
                case "Gt": pass = ld > rd; return true;
                case "Gte": pass = ld >= rd; return true;
                case "Lt": pass = ld < rd; return true;
                case "Lte": pass = ld <= rd; return true;
                case "Eq": pass = Math.Abs(ld - rd) < 1e-9; return true;
                case "Neq": pass = Math.Abs(ld - rd) >= 1e-9; return true;
                default:
                    error = "未知 Op: " + op;
                    return false;
            }
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
