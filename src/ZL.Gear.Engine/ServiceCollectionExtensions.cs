using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core;
using ZL.Gear.Core.Configuration;
using ZL.Gear.Core.Services;
using ZL.Gear.Engine.Runner;


namespace ZL.Gear.Engine
{
    /// <summary>
    /// Engine 层 DI 扩展方法集合。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 Gear Engine 核心服务到 <see cref="IServiceCollection"/>。
        /// </summary>
        /// <remarks>
        /// 设计要点：
        /// <list type="bullet">
        /// <item>注册项目库、Profile、StepCatalog 等核心服务；</item>
        /// <item>不注册 <see cref="IDeviceService"/>：Engine 解耦后不再引用 Drivers，须由宿主显式注入；</item>
        /// <item>纯逻辑宿主可通过 <see cref="SequenceExecutorBuilder.AsLogicOnlyDemoHost"/> 规避设备服务缺失；</item>
        /// <item>未声明宿主直接 <see cref="SequenceExecutorBuilder.Build"/> 会抛 <see cref="InvalidOperationException"/>（无 NullDeviceService 兜底）。</item>
        /// </list>
        /// </remarks>
        /// <param name="services">DI 服务集合。</param>
        /// <returns>便于链式注册的 <see cref="IServiceCollection"/>。</returns>
        public static IServiceCollection AddGearEngine(this IServiceCollection services)
        {
            // Configuration & Context
            services.AddSingleton<ILibraryService, LibraryService>();

            // Core Services
            services.AddSingleton<IGearProfileService, GearProfileServices>();
            services.AddSingleton<IStepCatalogService, StepCatalogService>();

            // 注意: IDeviceService 必须由宿主显式注册（Engine 解耦后不再引用 Drivers）。
            // 全栈 Demo/产线宿主：services.AddGearDrivers();  // ZL.Gear.Drivers 程序集
            // 纯逻辑宿主：显式调用 SequenceExecutorBuilder.Create().AsLogicOnlyDemoHost()；
            // 未声明宿主直接 Build() 会抛 InvalidOperationException（无 NullDeviceService 兜底）。

            // Engine Services
            services.AddTransient<SequenceExecutor>();
            services.AddTransient<GearRunner>();

            return services;
        }
    }
}
