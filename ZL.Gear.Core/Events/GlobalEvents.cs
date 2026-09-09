using System;
using System.Threading.Tasks;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Runner;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// [已过时] 全局事件静态代理。
    /// 请在构造函数中注入 IEventBus，并使用 EventBusExtensions 中的扩展方法。
    /// </summary>
    [Obsolete("请使用 IEventBus 依赖注入替代静态访问", false)]
    public static class GlobalEvents
    {
        private static readonly IEventBus _bus = DefaultEventBus.Instance;

        /// <summary>
        /// 公开的事件总线实例 (作为遗留代码过渡向依赖注入的临时通道)
        /// </summary>
        public static IEventBus Bus => _bus;
    }
}
