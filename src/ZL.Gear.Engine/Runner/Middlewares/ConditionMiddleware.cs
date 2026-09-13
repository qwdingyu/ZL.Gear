using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 条件执行中间件 (护城河特性)。
    /// 允许步骤根据表达式判断是否执行。
    /// 例如：Condition: "${Voltage} > 5.0"
    /// 这赋予了 ZL.Gear 在 JSON 层面进行复杂逻辑分支的能力。
    /// </summary>
    public class ConditionMiddleware : IStepMiddleware
    {
        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(StepConfig step, StepContext context, Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            if (step.Parameters != null && step.Parameters.TryGetValue("Condition", out var condObj) && condObj != null)
            {
                string condition = condObj.ToString();
                if (!string.IsNullOrWhiteSpace(condition))
                {
                    try
                    {
                        // 使用 context 中的 Evaluate 引擎进行布尔判定
                        bool shouldExecute = context.Evaluate(condition);
                        
                        if (!shouldExecute)
                        {
                            context.Log($"[Skip] 步骤 '{step.StepName}' 未满足执行条件: {condition}");
                            return ExecutionResult<List<Measurement>>.Skipped(
                                $"[ConditionSkip] 条件未满足: {condition}",
                                new List<Measurement>());
                        }
                    }
                    catch (Exception ex)
                    {
                        context.Log($"[Error] 条件表达式评估失败: {condition}, 错误: {ex.Message}");
                        // 表达式错误视为失败，以保证安全
                        return ExecutionResult<List<Measurement>>.Failed($"Condition Evaluation Failed: {ex.Message}");
                    }
                }
            }

            return await next(step, context).ConfigureAwait(false);
        }
    }
}
