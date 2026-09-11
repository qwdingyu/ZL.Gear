using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core;
using ZL.Gear.Core.Configuration;
using ZL.Gear.Core.Services;
using ZL.Gear.Engine.Runner;


namespace ZL.Gear.Engine
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGearEngine(this IServiceCollection services)
        {
            // Configuration & Context
            services.AddSingleton<ILibraryService, LibraryService>();
            
            // Core Services
            services.AddSingleton<IGearProfileService, GearProfileServices>();
            services.AddSingleton<IStepCatalogService, StepCatalogService>();
            
            // 注意: IDeviceService 必须由宿主显式注册（Engine 解耦后不再引用 Drivers）。
            // 全栈 Demo/产线宿主：services.AddGearDrivers();  // ZL.Gear.Drivers 程序集
            // 纯逻辑宿主：省略即可，SequenceExecutorBuilder 默认 NullDeviceService。
            
            // Engine Services
            services.AddTransient<SequenceExecutor>();
            services.AddTransient<GearRunner>();
            
            return services;
        }
    }
}
