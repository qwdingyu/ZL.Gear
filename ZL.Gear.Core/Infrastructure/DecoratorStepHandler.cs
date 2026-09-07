using System;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 装饰器模式基类：用于在不修改现有 Handler 的情况下，添加横切关注点（如日志、指标、权限等）。
    /// 职责：包装一个 <see cref="IStepHandler"/>，在调用其 <see cref="ExecuteAsync"/> 前后执行额外逻辑。
    /// 设计要点：
    /// 1. 保持与 <see cref="IStepHandler"/> 接口兼容；
    /// 2. 子类可重写 <see cref="OnBeforeExecuteAsync"/> 和 <see cref="OnAfterExecuteAsync"/> 插入自定义逻辑；
    /// 3. 支持多层装饰，形成装饰器链。
    /// </summary>
    public abstract class DecoratorStepHandler : IStepHandler
    {
        /// <summary>
        /// 被装饰的 Handler。
        /// </summary>
        protected IStepHandler InnerHandler { get; }

        /// <summary>
        /// 初始化装饰器。
        /// </summary>
        /// <param name="innerHandler">被装饰的 Handler，不能为 null。</param>
        /// <exception cref="ArgumentNullException">innerHandler 为 null。</exception>
        protected DecoratorStepHandler(IStepHandler innerHandler)
        {
            InnerHandler = innerHandler ?? throw new ArgumentNullException(nameof(innerHandler));
        }

        /// <summary>
        /// 执行步骤（装饰器入口）。
        /// 执行顺序：Before → Inner.ExecuteAsync → After。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="context">执行上下文。</param>
        /// <returns>执行结果。</returns>
        public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            await OnBeforeExecuteAsync(step, context);
            var result = await InnerHandler.ExecuteAsync(step, context);
            await OnAfterExecuteAsync(step, context, result);
            return result;
        }

        /// <summary>
        /// 调用内部 Handler 之前执行。
        /// 可用于：日志记录、权限检查、指标采集、参数校验等。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="context">执行上下文。</param>
        protected virtual Task OnBeforeExecuteAsync(StepConfig step, StepContext context)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 调用内部 Handler 之后执行。
        /// 可用于：结果审计、性能统计、异常上报、状态更新等。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="context">执行上下文。</param>
        /// <param name="result">内部 Handler 的执行结果。</param>
        protected virtual Task OnAfterExecuteAsync(StepConfig step, StepContext context, ExecutionResultBase result)
        {
            return Task.CompletedTask;
        }
    }
}
