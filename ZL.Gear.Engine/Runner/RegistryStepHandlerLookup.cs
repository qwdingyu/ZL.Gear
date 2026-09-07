using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 基于注册表的步骤处理器查找策略。
    /// 职责：根据 <see cref="StepConfig.Command"/> 决定使用哪个处理器。
    /// 查找顺序：注册表 → 模板 → 通用回退。
    /// 设计要点：
    /// 1. 模板和回退策略通过 <see cref="IStepHandlerProvider"/> 外部化，不再硬编码；
    /// 2. 注册表使用 <see cref="ConcurrentDictionary{TKey, TValue}"/>，支持并发读写；
    /// 3. 提供默认构造函数保持向后兼容，也支持通过自定义 Provider 替换策略。
    /// </summary>
    public class RegistryStepHandlerLookup : IStepHandlerLookup, IRegisterableStepHandlerLookup
    {
        /// <summary>
        /// 专用 Handler 注册表，线程安全，支持并发读写。
        /// 键为命令名称，值为对应的 Handler 实例。
        /// </summary>
        private readonly ConcurrentDictionary<string, IStepHandler> _handlers = new();

        /// <summary>
        /// Handler 提供者：负责提供模板 Handler 和回退 Handler。
        /// 不再硬编码 <see cref="TemplateFlowHandler"/> 和 <see cref="CommonHandler"/>，而是委托给 Provider。
        /// </summary>
        private readonly IStepHandlerProvider _handlerProvider;

        /// <summary>
        /// Handler 工厂：用于创建插件目录扫描到的 Handler 实例。
        /// </summary>
        private readonly IStepHandlerFactory _handlerFactory;

        /// <summary>
        /// 使用默认 Handler 提供者初始化。
        /// </summary>
        /// <param name="handlerFactory">Handler 工厂，用于插件扫描。</param>
        /// <param name="templateRootDir">模板根目录，为 null 时使用默认路径。</param>
        public RegistryStepHandlerLookup(
            IStepHandlerFactory handlerFactory,
            string templateRootDir = null)
            : this(handlerFactory, new DefaultStepHandlerProvider(templateRootDir))
        {
        }

        /// <summary>
        /// 使用自定义 Handler 提供者初始化。
        /// 允许完全替换模板/回退策略，例如用自定义 DSL 引擎替换 <see cref="TemplateFlowHandler"/>。
        /// </summary>
        /// <param name="handlerFactory">Handler 工厂，用于插件扫描。</param>
        /// <param name="handlerProvider">Handler 提供者，提供模板和回退 Handler。</param>
        /// <exception cref="ArgumentNullException">handlerFactory 或 handlerProvider 为 null。</exception>
        public RegistryStepHandlerLookup(
            IStepHandlerFactory handlerFactory,
            IStepHandlerProvider handlerProvider)
        {
            _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
            _handlerProvider = handlerProvider ?? throw new ArgumentNullException(nameof(handlerProvider));
        }

        /// <summary>
        /// 尝试查找专用 Handler（注册表查找）。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="handler">如果找到，输出专用 Handler。</param>
        /// <returns>是否找到专用 Handler。</returns>
        public bool TryGetHandler(StepConfig step, out IStepHandler handler)
        {
            return _handlers.TryGetValue(step.Command, out handler);
        }

        /// <summary>
        /// 尝试查找模板 Handler（委托给 Provider）。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="handler">如果找到，输出模板 Handler。</param>
        /// <returns>是否找到可用模板。</returns>
        public bool TryGetTemplateHandler(StepConfig step, out IStepHandler handler)
        {
            return _handlerProvider.TryGetTemplateHandler(step, out handler);
        }

        /// <summary>
        /// 获取通用回退 Handler（委托给 Provider）。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <returns>回退 Handler 实例。</returns>
        public IStepHandler GetFallbackHandler(StepConfig step)
        {
            return _handlerProvider.GetFallbackHandler(step);
        }

        /// <summary>
        /// 注册专用 Handler 到注册表。
        /// </summary>
        /// <param name="command">命令名称。</param>
        /// <param name="handler">Handler 实例。</param>
        /// <param name="allowOverwrite">是否允许覆盖已注册的 Handler。</param>
        /// <exception cref="InvalidOperationException">当 allowOverwrite 为 false 且命令已注册时抛出。</exception>
        public void Register(string command, IStepHandler handler, bool allowOverwrite = true)
        {
            if (allowOverwrite)
            {
                // 允许覆盖：直接赋值，线程安全
                _handlers[command] = handler;
            }
            else if (!_handlers.TryAdd(command, handler))
            {
                // 不允许覆盖：使用 TryAdd 原子操作，失败则抛异常
                throw new InvalidOperationException($"命令 '{command}' 已经被注册。");
            }
        }

        /// <summary>
        /// 从注册表中移除指定命令的 Handler。
        /// 注意：不会释放 Handler 实例本身，仅移除引用。
        /// 如果 Handler 实现了 <see cref="IDisposable"/>，调用方需自行负责释放。
        /// </summary>
        /// <param name="command">命令名称。</param>
        /// <returns>是否成功移除。</returns>
        public bool Unregister(string command)
        {
            return _handlers.TryRemove(command, out _);
        }

        /// <summary>
        /// 获取所有已注册的命令名称。
        /// </summary>
        /// <returns>已注册命令的只读集合。</returns>
        public IEnumerable<string> GetRegisteredCommands()
        {
            return _handlers.Keys.ToList();
        }

        /// <summary>
        /// 移除所有以指定前缀开头的命令（用于插件卸载）。
        /// </summary>
        /// <param name="prefix">命令前缀，如 "MyPlugin."。</param>
        /// <returns>实际移除的 Handler 数量。</returns>
        public int RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                throw new ArgumentNullException(nameof(prefix));

            int removedCount = 0;
            foreach (var key in _handlers.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (_handlers.TryRemove(key, out _))
                    {
                        removedCount++;
                    }
                }
            }

            return removedCount;
        }

        /// <summary>
        /// 移除所有以指定前缀开头的命令，并返回被移除的 Handler 实例（用于资源释放）。
        /// </summary>
        /// <param name="prefix">命令前缀，如 "MyPlugin."。</param>
        /// <param name="removedHandlers">被移除的 Handler 实例列表。</param>
        /// <returns>实际移除的 Handler 数量。</returns>
        public int RemoveByPrefix(string prefix, out List<IStepHandler> removedHandlers)
        {
            removedHandlers = new List<IStepHandler>();
            if (string.IsNullOrEmpty(prefix))
                throw new ArgumentNullException(nameof(prefix));

            int removedCount = 0;
            foreach (var key in _handlers.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (_handlers.TryRemove(key, out var handler))
                    {
                        removedHandlers.Add(handler);
                        removedCount++;
                    }
                }
            }

            return removedCount;
        }

        /// <summary>
        /// 获取已注册的 Handler 数量（用于监控/诊断）。
        /// </summary>
        public int RegisteredCount => _handlers.Count;
    }
}
