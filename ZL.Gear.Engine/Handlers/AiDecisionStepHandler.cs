using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core.Abstractions;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Devices; // Corrected namespace

namespace ZL.Gear.Engine.Handlers
{
    /// <summary>
    /// AI 决策步骤处理器。
    /// 该处理器不直接执行设备动作，而是调用 IAiDecisionPolicy 获取决策，
    /// 并根据决策结果更新上下文变量或控制流程。
    /// </summary>
    public class AiDecisionStepHandler : IStepHandler
    {
        public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            // 1. 获取 AI 策略服务
            // 优先从 DI 容器获取，如果未注册则报错
            var policy = context.GetService<IAiDecisionPolicy>();
            if (policy == null)
            {
                return ExecutionResult.Failed("未找到 IAiDecisionPolicy 服务。请在启动时注册 AI 策略实现。");
            }

            context.Log($"[AI] 正在调用决策策略: {policy.GetType().Name}...");

            try
            {
                // 2. 执行决策
                var decision = await policy.DecideAsync(context, context.CancellationToken);

                context.Log($"[AI] 决策结果: {decision.NextAction}, 理由: {decision.Reason}, 置信度: {decision.Confidence:P0}");

                // 3. 应用决策影响
                // 将决策结果写入变量，供后续步骤使用。
                // 必须用 SetShared 写到**流程级**作用域：DynamicFlow 的节点在 CreateChildScope() 子作用域中执行，
                // 默认 Set 只写节点级隔离作用域，后继 Assert/WaitUntil/Calculate 会读不到（docs/141 G-07 · docs/136）。
                context.Variables.SetShared("AI_LastAction", decision.NextAction);
                context.Variables.SetShared("AI_LastReason", decision.Reason);
                
                if (decision.NewParameters != null)
                {
                    foreach (var kv in decision.NewParameters)
                    {
                        context.Variables.SetShared(kv.Key, kv.Value);
                        context.Log($"[AI] 更新变量: {kv.Key} = {kv.Value}");
                    }
                }

                // 4. 根据 Action 返回结果
                // 这里定义简单的映射逻辑，更复杂的逻辑应由 DynamicFlowHandler 或外部编排器处理
                if (decision.NextAction == "Abort")
                {
                    return ExecutionResult.Failed($"AI 决定终止流程: {decision.Reason}");
                }
                
                // 默认视为成功
                return ExecutionResult.Succeeded($"AI 决策完成: {decision.NextAction}");
            }
            catch (Exception ex)
            {
                return ExecutionResult.Failed($"AI 决策执行异常: {ex.Message}");
            }
        }
    }
}
