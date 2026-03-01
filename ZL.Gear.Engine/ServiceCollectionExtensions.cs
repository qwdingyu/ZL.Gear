using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core;
using ZL.Gear.Core.Services;
using ZL.Gear.Engine.Runner;


namespace ZL.Gear.Engine
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGearEngine(this IServiceCollection services)
        {
            // Core Services
            services.AddSingleton<IGearProfileService, GearProfileServices>();
            
            // 注意: IDeviceService 必须由宿主程序注册（因为它依赖具体的驱动实现）
            // services.AddSingleton<IDeviceService, ...>();
            
            // Engine Services
            services.AddTransient<SequenceExecutor>();
            services.AddTransient<GearRunner>();
            
            return services;
        }
    }
}
