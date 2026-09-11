using System;
using System.Collections.Generic;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Engine.Runner.Middlewares;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 中间件管道构建器。
    /// 职责：通过链式 API 组合 <see cref="IStepMiddleware"/>，构建可执行的 <see cref="StepExecutionPipeline"/>。
    /// 设计要点：
    /// 1. 参考 ASP.NET Core 的 <see cref="Microsoft.AspNetCore.Builder.IApplicationBuilder"/> 设计；
    /// 2. 提供 Production/Simulation/Lab 三种预设管道，覆盖工控常见场景；
    /// 3. 也支持通过 <see cref="Use(IStepMiddleware)"/> 自由扩展，不限制只能使用预设。
    /// </summary>
    public class StepPipelineBuilder
    {
        /// <summary>
        /// 中间件列表，按添加顺序执行。
        /// </summary>
        private readonly List<IStepMiddleware> _middlewares = new();

        /// <summary>
        /// 日志输出委托，传递给需要日志的中间件。
        /// </summary>
        private Action<string> _log = s => { };

        /// <summary>
        /// 设置日志输出委托。
        /// </summary>
        /// <param name="log">日志输出函数，为 null 时使用空委托。</param>
        /// <returns>构建器实例，支持链式调用。</returns>
        public StepPipelineBuilder UseLogger(Action<string> log)
        {
            _log = log ?? (s => { });
            return this;
        }

        /// <summary>
        /// 添加自定义中间件到管道。
        /// </summary>
        /// <param name="middleware">中间件实例。</param>
        /// <returns>构建器实例，支持链式调用。</returns>
        /// <exception cref="ArgumentNullException">middleware 为 null。</exception>
        public StepPipelineBuilder Use(IStepMiddleware middleware)
        {
            if (middleware == null) throw new ArgumentNullException(nameof(middleware));
            _middlewares.Add(middleware);
            return this;
        }

        /// <summary>
        /// 使用产线生产场景的默认全功能管道。
        /// 包含：日志、审计、熔断、条件、安全、延迟、资源锁、重试、快照、诊断、变量追踪。
        /// 注意：步骤超时由 <see cref="StepDispatcher"/> 的 DispatchCore 统一施加，不再叠加 TimeoutMiddleware，避免双 CTS。
        /// </summary>
        /// <returns>构建器实例，支持链式调用。</returns>
        public StepPipelineBuilder UseDefaultPipeline()
        {
            Use(new LoggingMiddleware(_log));
            Use(new AuditLogMiddleware(_log));
            Use(new CircuitBreakerMiddleware(_log));
            Use(new ConditionMiddleware());
            Use(new SafetyCheckMiddleware(_log));
            Use(new StepDelayMiddleware(_log));
            // 超时权威在 StepDispatcher.DispatchCoreAsync（支持 TimeoutMs / TimeoutAction）
            Use(new ResourceLockMiddleware());
            Use(new RetryMiddleware());
            Use(new SnapshotMiddleware(_log));
            Use(new DiagnosticsMiddleware(_log));
            Use(new VariableTraceMiddleware());
            return this;
        }

        /// <summary>
        /// 使用仿真场景的轻量管道。
        /// 跳过：熔断、审计、快照、诊断、安全、变量追踪。
        /// 保留：日志、条件、延迟、资源锁。超时由 DispatchCore 统一处理。
        /// </summary>
        /// <returns>构建器实例，支持链式调用。</returns>
        public StepPipelineBuilder UseSimulationPipeline()
        {
            Use(new LoggingMiddleware(_log));
            Use(new ConditionMiddleware());
            Use(new StepDelayMiddleware(_log));
            Use(new ResourceLockMiddleware());
            return this;
        }

        /// <summary>
        /// 使用实验室场景的管道。
        /// 跳过：熔断、重试、快照。
        /// 保留：日志、审计、条件、安全、延迟、资源锁、诊断、变量追踪。超时由 DispatchCore 统一处理。
        /// </summary>
        /// <returns>构建器实例，支持链式调用。</returns>
        public StepPipelineBuilder UseLabPipeline()
        {
            Use(new LoggingMiddleware(_log));
            Use(new AuditLogMiddleware(_log));
            Use(new ConditionMiddleware());
            Use(new SafetyCheckMiddleware(_log));
            Use(new StepDelayMiddleware(_log));
            Use(new ResourceLockMiddleware());
            Use(new DiagnosticsMiddleware(_log));
            Use(new VariableTraceMiddleware());
            return this;
        }

        /// <summary>
        /// 构建可执行的管道实例。
        /// </summary>
        /// <returns>已配置完成的 <see cref="StepExecutionPipeline"/> 实例。</returns>
        public StepExecutionPipeline Build()
        {
            var pipeline = new StepExecutionPipeline();
            foreach (var middleware in _middlewares)
            {
                pipeline.Use(middleware);
            }
            return pipeline;
        }
    }
}
