using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;

namespace ZL.Gear.Core.Workflow
{
    /// <summary>
    /// StepContext中默认的ActionResolver
    /// </summary>
    public static class WorkflowGlobal
    {
        private static IServiceProvider? _services;
        private static readonly object _lock = new object();
        private static bool _initialized = false;

        /// <summary>
        /// 持有全局的 ServiceProvider
        /// </summary>
        public static IServiceProvider Services
        {
            get
            {
                if (_services == null)
                {
                    throw new InvalidOperationException("WorkflowGlobal 尚未初始化，请先调用 Initialize()");
                }
                return _services;
            }
        }

        /// <summary>
        /// 程序启动时调用一次（线程安全）
        /// </summary>
        public static void Initialize(IServiceProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (_lock)
            {
                if (!_initialized)
                {
                    _services = provider;
                    _initialized = true;
                }
                // 如果已经初始化，忽略重复调用（保持第一次初始化的配置）
            }
        }

        /// <summary>
        /// 重置全局状态（主要用于测试）
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _services = null;
                _initialized = false;
            }
        }

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        public static bool IsInitialized => _initialized && _services != null;

        // === 向后兼容的快捷属性 ===

        // 以前你是直接访问 WorkflowGlobal.Resolver
        // 现在变成从容器中获取，外部代码几乎不用改
        public static IActionResolver Resolver => Services.GetRequiredService<IActionResolver>();

        // 你还可以暴露 Logger
        public static Action<string>? Logger => Services.GetService<Action<string>>();
    }
}
