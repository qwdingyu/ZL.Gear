using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine.Runner;
using ZL.Gear.Engine.Workflow;
using ZL.Gear.Sensing;

namespace ZL.Gear.Samples.Industry.Client.Bootstrap
{
    /// <summary>
    /// 盐城座椅产线宿主 Bootstrap 参考实现（无 UI）。
    /// 文档：ZL.Gear.Docs/175 §十四 · legacy SeatTest/Program.cs + NoiseService.cs + ModelStepService.cs
    /// </summary>
    /// <remarks>
    /// IndustryKit 默认走 DynamicFlow + StationExtension；本类展示 legacy StepConfig 树路径的启动顺序。
    /// Noise 通道通过 <see cref="RegisterNoiseSession"/> 注册 SampleEvents（legacy NoiseService 等价）。
    /// </remarks>
    public sealed class SeatProductionHostBootstrap
    {
        public IServiceProvider Services { get; private set; } = null!;

        /// <summary>legacy ModelStepService 等价：顶层步骤强制 Verify。</summary>
        public static void ApplyElectricalTestVerifyPolicy(IList<StepConfig> topLevelSteps)
        {
            if (topLevelSteps == null) return;
            foreach (var step in topLevelSteps)
            {
                step.ExecutionType = StepExecutionType.Verify;
            }
        }

        /// <summary>
        /// 构建 DI + 加载 Extension.Seat 等价模块。
        /// legacy: Program.cs InitializeGlobalEnvironment → LoadModules(Extension.Seat)
        /// </summary>
        public SeatProductionHostBootstrap ConfigureServices(Action<IServiceCollection>? configure = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<WorkflowActionService>();
            services.AddSingleton<IActionResolver>(sp => sp.GetRequiredService<WorkflowActionService>());
            services.AddSingleton<IActionRegistry>(sp => sp.GetRequiredService<WorkflowActionService>());
            services.AddSingleton<StepDispatcher>();
            services.AddSingleton<ISbrLocationProvider>(_ => new InMemorySbrLocationProvider(DefaultSbrLocations()));
            services.AddSingleton<IStationInteractionPort, NullStationInteractionPort>();
            configure?.Invoke(services);
            Services = services.BuildServiceProvider();
            WorkflowGlobal.Initialize(Services);
            return this;
        }

        /// <summary>加载行业扩展程序集（替换为 ZL.Gear.Extension.Seat 程序集名）。</summary>
        public SeatProductionHostBootstrap LoadSeatExtension(string assemblyName = "ZL.Gear.Extension.Seat")
        {
            var dispatcher = Services.GetRequiredService<StepDispatcher>();
            dispatcher.LoadModules(Assembly.Load(assemblyName));
            return this;
        }

        /// <summary>
        /// 注册 Noise 采样通道。
        /// legacy: SampleEvents.SessionRequesters["Noise"] = () => _noiseService.BeginSamplingAsync()...
        /// </summary>
        public SeatProductionHostBootstrap RegisterNoiseSession(
            Func<Task<ISamplingSession<double>>>? beginSampling = null,
            Action<string>? log = null)
        {
            beginSampling ??= () => InMemoryNoiseSessionFactory.BeginSamplingAsync();
            SampleEvents.SessionRequesters["Noise"] = () =>
                beginSampling().ContinueWith(t => (object)t.Result);
            log?.Invoke("[bootstrap] Noise 通道已注册（SampleEvents.SessionRequesters[\"Noise\"]）");
            return this;
        }

        [Obsolete("改用 RegisterNoiseSession")]
        public SeatProductionHostBootstrap RegisterNoiseSessionPlaceholder(Action<string>? log = null) =>
            RegisterNoiseSession(log: log);

        /// <summary>legacy Frm_SeatTest.cs:199 — DeviceServices.sbrLocDict = GlobalV.sbrLocDict</summary>
        public static IReadOnlyDictionary<string, float> DefaultSbrLocations() =>
            new Dictionary<string, float>
            {
                ["1"] = 0f, ["2"] = 0f, ["3"] = 0f, ["4"] = 0f,
                ["5"] = 0f, ["6"] = 0f, ["7"] = 0f, ["8"] = 0f
            };

        /// <summary>推荐启动顺序（文档化，供宿主 Main 调用）。</summary>
        public static SeatProductionHostBootstrap CreateDefault(string? extensionAssembly = null)
        {
            var bootstrap = new SeatProductionHostBootstrap()
                .ConfigureServices()
                .RegisterNoiseSession(log: Console.WriteLine);

            if (!string.IsNullOrEmpty(extensionAssembly))
            {
                bootstrap.LoadSeatExtension(extensionAssembly);
            }

            return bootstrap;
        }
    }
}
