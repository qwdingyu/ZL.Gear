using System;
using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Services;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// Microsoft.Extensions.DependencyInjection 扩展方法
    /// </summary>
    public static class GearEngineServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 Gear Engine 核心服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configure">配置选项</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddGearEngine(this IServiceCollection services, Action<GearEngineOptions> configure)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            var options = new GearEngineOptions();
            configure(options);

            // 注册日志委托
            services.AddSingleton(options.Logger ?? (_ => { }));

            // 注册场景化管道提供器
            services.AddSingleton<IScenarioPipelineProvider>(sp =>
            {
                var handlerFactory = new DefaultStepHandlerFactory();
                return new DefaultScenarioPipelineProvider(handlerFactory);
            });

            // 注册动作注册表
            services.AddSingleton<SimpleActionRegistry>();
            services.AddSingleton<IActionRegistry>(sp => sp.GetRequiredService<SimpleActionRegistry>());
            services.AddSingleton<IActionResolver>(sp => sp.GetRequiredService<SimpleActionRegistry>());

            // 注册 StepDispatcher
            // 关键点：RegistryStepHandlerLookup 现在通过 DefaultStepHandlerProvider 外部化模板/回退策略
            // 这样即使后续替换 DSL 引擎或回退逻辑，也不影响 StepDispatcher 构造函数签名
            services.AddSingleton(sp =>
            {
                var actionRegistry = sp.GetRequiredService<IActionRegistry>();
                var log = sp.GetRequiredService<Action<string>>();
                var pipelineProvider = sp.GetRequiredService<IScenarioPipelineProvider>();
                var pipeline = pipelineProvider.GetPipeline(options.Scenario, log);

                IStepHandlerFactory handlerFactory;
                if (options.UseDiFactory && sp.GetService(typeof(IStepHandlerFactory)) is IStepHandlerFactory diFactory)
                {
                    handlerFactory = diFactory;
                }
                else
                {
                    handlerFactory = new DefaultStepHandlerFactory();
                }

                return new StepDispatcher(
                    new RegistryStepHandlerLookup(
                        handlerFactory,
                        new DefaultStepHandlerProvider()),
                    handlerFactory,
                    actionRegistry,
                    pipeline,
                    log,
                    builtInModules: options.BuiltInModules);
            });

            return services;
        }
    }

    /// <summary>
    /// Gear Engine 配置选项
    /// </summary>
    public class GearEngineOptions
    {
        /// <summary>
        /// 日志输出委托
        /// </summary>
        public Action<string> Logger { get; set; }

        /// <summary>
        /// 执行场景
        /// </summary>
        public ExecutionScenario Scenario { get; set; } = ExecutionScenario.Production;

        /// <summary>
        /// 是否使用 DI 工厂创建 Handler
        /// </summary>
        public bool UseDiFactory { get; set; } = false;

        /// <summary>
        /// 内置模块掩码（默认 All = Core+Sensing+Plc+Ai）。
        /// 瘦宿主可设为 <see cref="BuiltInModules.Core"/>；见 docs/138。
        /// </summary>
        public BuiltInModules BuiltInModules { get; set; } = BuiltInModules.All;
    }
}
