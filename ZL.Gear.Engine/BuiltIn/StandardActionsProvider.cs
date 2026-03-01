using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
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

            registry.RegisterAction("SetVariable", async (step, ctx) =>
            {
                var key = ctx.Get<string>("Key");
                var val = ctx.Get<object>("Value");
                if (!string.IsNullOrEmpty(key))
                {
                    ctx.Variables.Set(key, val);
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
                var expected = ctx.Get<bool>("Condition", true);
                if (!expected)
                {
                    var msg = ctx.Get<string>("Message") ?? "断言失败";
                    return ExecutionResult.Failed(msg);
                }
                return ExecutionResult.Succeeded();
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
            if (step.Parameters != null && step.Parameters.TryGetValue("Duration", out var val))
            {
                if (int.TryParse(val?.ToString(), out int d)) delayMs = d;
            }

            ctx.Log($"-> 系统延时: {delayMs}ms");
            await Task.Delay(delayMs, ctx.CancellationToken);
            return ExecutionResult.Succeeded();
        }
    }
}
