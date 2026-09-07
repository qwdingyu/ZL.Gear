using System;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 基于 Microsoft.Extensions.DependencyInjection 的 Handler 工厂
    /// 从 IServiceProvider 解析 Handler 实例
    /// </summary>
    public class DiStepHandlerFactory : IStepHandlerFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public DiStepHandlerFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public IStepHandler CreateHandler(Type handlerType)
        {
            if (handlerType == null) throw new ArgumentNullException(nameof(handlerType));
            if (!typeof(IStepHandler).IsAssignableFrom(handlerType))
                throw new ArgumentException($"类型 {handlerType.FullName} 未实现 IStepHandler", nameof(handlerType));

            var handler = _serviceProvider.GetService(handlerType) as IStepHandler;
            if (handler == null)
                throw new InvalidOperationException($"无法从 IServiceProvider 解析类型 {handlerType.FullName}");

            return handler;
        }

        public IStepHandler CreateHandler(Type handlerType, params object[] args)
        {
            if (handlerType == null) throw new ArgumentNullException(nameof(handlerType));
            if (!typeof(IStepHandler).IsAssignableFrom(handlerType))
                throw new ArgumentException($"类型 {handlerType.FullName} 未实现 IStepHandler", nameof(handlerType));

            // DI 容器通常不直接支持带参数解析，回退到反射
            var ctor = handlerType.GetConstructor(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null,
                Array.ConvertAll(args, a => a?.GetType() ?? typeof(object)),
                null);

            if (ctor == null)
                throw new InvalidOperationException($"类型 {handlerType.FullName} 没有匹配的构造函数");

            return (IStepHandler)ctor.Invoke(args);
        }
    }
}
