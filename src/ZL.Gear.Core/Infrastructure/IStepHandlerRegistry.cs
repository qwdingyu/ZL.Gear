using System;
using System.Collections.Generic;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 步骤处理器注册表接口。
    /// 职责：定义“谁”有资格接收命令注册，是插件、扩展模块与核心框架之间的契约。
    /// </summary>
    public interface IStepHandlerRegistry
    {
        /// <summary>
        /// 注册步骤处理器（仅用于内部实现，公共 API 请使用 <see cref="RegisterHandlerWithAction"/>）。
        /// </summary>
        /// <param name="command">命令名称，用于步骤配置中的 Command 字段匹配。</param>
        /// <param name="handler">处理器实例。</param>
        /// <param name="allowOverwrite">是否允许覆盖已注册的同名命令处理器。</param>
        void RegisterHandler(string command, IStepHandler handler, bool allowOverwrite = true);

        /// <summary>
        /// 统一注册步骤处理器与对应的原子动作，避免调用方遗漏双注册中的任意一侧。
        /// </summary>
        /// <param name="command">命令名称。</param>
        /// <param name="handler">处理器实例。</param>
        /// <param name="allowOverwrite">是否允许覆盖已注册的 Handler 与 Action。</param>
        void RegisterHandlerWithAction(string command, IStepHandler handler, bool allowOverwrite = true);

        /// <summary>
        /// 获取所有已注册的命令名称（用于启动期冲突扫描）。
        /// </summary>
        /// <returns>已注册命令的只读集合。</returns>
        IEnumerable<string> GetRegisteredCommands();

        /// <summary>
        /// 获取指定命令对应的 EvaluateResult 元数据（若 Handler 侧通过 <see cref="StepHandlerCommandAttribute"/> 标注）。
        /// </summary>
        /// <param name="command">命令名称。</param>
        /// <returns>若存在元数据则返回其值，否则返回 null。</returns>
        bool? GetEvaluateResult(string command);

        /// <summary>
        /// 显式设置命令级 EvaluateResult 元数据（legacy 旁路命令等，Handler 类无法单独标注时使用）。
        /// </summary>
        /// <param name="command">命令名称。</param>
        /// <param name="evaluateResult">false 表示跳过 ResultEvaluator，直接采用 Handler 成败。</param>
        void SetEvaluateResult(string command, bool evaluateResult);
    }

    /// <summary>
    /// 扩展模块启动接口。
    /// 每个外部扩展 DLL 可通过实现此接口获得一个明确的初始化入口，
    /// 由 <see cref="ZL.Gear.Engine.ModuleLoader"/> 在加载时调用。
    /// </summary>
    public interface IGearExtension
    {
        /// <summary>
        /// 扩展初始化时调用。
        /// </summary>
        /// <param name="registry">核心框架传入的注册能力，用于向调度器注册 Handler。</param>
        void Initialize(IStepHandlerRegistry registry);
    }

    /// <summary>
    /// 支持依赖注入的扩展模块启动接口。
    /// 在 <see cref="ZL.Gear.Engine.ModuleLoader"/> 检测到宿主已提供 <see cref="IServiceProvider"/> 时，
    /// 将优先调用此接口，使扩展模块可从容器中解析其他服务。
    /// </summary>
    public interface IDiGearExtension : IGearExtension
    {
        /// <summary>
        /// 扩展初始化时调用（带服务提供者）。
        /// </summary>
        /// <param name="registry">核心框架传入的注册能力。</param>
        /// <param name="serviceProvider">服务提供者，用于获取其他服务。</param>
        void Initialize(IStepHandlerRegistry registry, IServiceProvider serviceProvider);
    }

    /// <summary>
    /// 步骤处理器查找策略。
    /// 根据 <see cref="StepConfig"/> 决定使用哪个处理器，是 StepDispatcher 的核心抽象之一。
    /// 查找顺序通常为：注册表 → 模板 → 通用回退。
    /// </summary>
    public interface IStepHandlerLookup
    {
        /// <summary>
        /// 尝试查找专用 Handler（注册表查找）。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="handler">如果找到，输出专用 Handler。</param>
        /// <returns>是否找到专用 Handler。</returns>
        bool TryGetHandler(StepConfig step, out IStepHandler handler);

        /// <summary>
        /// 尝试查找模板 Handler（如 JSON DSL 模板）。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="handler">如果找到，输出模板 Handler。</param>
        /// <returns>是否找到可用模板。</returns>
        bool TryGetTemplateHandler(StepConfig step, out IStepHandler handler);

        /// <summary>
        /// 获取通用回退 Handler（当没有专用 Handler 和模板时使用）。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <returns>回退 Handler 实例。</returns>
        IStepHandler GetFallbackHandler(StepConfig step);
    }

    /// <summary>
    /// 可注册的 Handler 查找策略。
    /// 在 <see cref="IStepHandlerLookup"/> 基础上增加注册与卸载能力，通常由 <see cref="RegistryStepHandlerLookup"/> 实现。
    /// </summary>
    public interface IRegisterableStepHandlerLookup : IStepHandlerLookup
    {
        /// <summary>
        /// 注册专用 Handler 到查找策略中。
        /// </summary>
        /// <param name="command">命令名称。</param>
        /// <param name="handler">Handler 实例。</param>
        /// <param name="allowOverwrite">是否允许覆盖已注册的 Handler。</param>
        void Register(string command, IStepHandler handler, bool allowOverwrite = true);

        /// <summary>
        /// 从查找策略中移除指定命令的 Handler。
        /// </summary>
        /// <param name="command">命令名称。</param>
        /// <returns>是否成功移除。</returns>
        bool Unregister(string command);

        /// <summary>
        /// 获取所有已注册的命令名称（用于启动期冲突扫描）。
        /// </summary>
        /// <returns>已注册命令的只读集合。</returns>
        IEnumerable<string> GetRegisteredCommands();

        /// <summary>
        /// 移除所有以指定前缀开头的命令（用于插件卸载）。
        /// </summary>
        /// <param name="prefix">命令前缀，如 "MyPlugin."。</param>
        /// <returns>实际移除的 Handler 数量。</returns>
        int RemoveByPrefix(string prefix);

        /// <summary>
        /// 移除所有以指定前缀开头的命令，并返回被移除的 Handler 实例（用于资源释放）。
        /// </summary>
        /// <param name="prefix">命令前缀，如 "MyPlugin."。</param>
        /// <param name="removedHandlers">被移除的 Handler 实例列表。</param>
        /// <returns>实际移除的 Handler 数量。</returns>
        int RemoveByPrefix(string prefix, out List<IStepHandler> removedHandlers);
    }

    /// <summary>
    /// 步骤处理器工厂。
    /// 负责创建 Handler 实例，是 StepDispatcher 的另一个核心抽象。
    /// 核心层提供 <see cref="DefaultStepHandlerFactory"/>（纯反射），
    /// 宿主层可按需提供基于 DI 容器的 <see cref="ZL.Gear.Engine.Runner.DiStepHandlerFactory"/>。
    /// </summary>
    public interface IStepHandlerFactory
    {
        /// <summary>
        /// 创建 Handler 实例（优先使用无参构造函数）。
        /// </summary>
        /// <param name="handlerType">Handler 类型，必须实现 <see cref="IStepHandler"/>。</param>
        /// <returns>Handler 实例。</returns>
        IStepHandler CreateHandler(Type handlerType);

        /// <summary>
        /// 创建 Handler 实例（带参数）。
        /// </summary>
        /// <param name="handlerType">Handler 类型，必须实现 <see cref="IStepHandler"/>。</param>
        /// <param name="args">构造函数参数。</param>
        /// <returns>Handler 实例。</returns>
        IStepHandler CreateHandler(Type handlerType, params object[] args);
    }

    /// <summary>
    /// 步骤处理器提供者。
    /// 负责提供模板 Handler 和回退 Handler，将 <see cref="RegistryStepHandlerLookup"/> 内部硬编码的策略外部化。
    /// 通过此接口，宿主可以自定义模板引擎或回退逻辑，而无需修改 RegistryStepHandlerLookup 的构造函数签名。
    /// </summary>
    public interface IStepHandlerProvider
    {
        /// <summary>
        /// 尝试获取模板 Handler（根据 StepConfig 判断是否有可用模板）。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="handler">如果找到模板，输出模板 Handler。</param>
        /// <returns>是否找到可用模板。</returns>
        bool TryGetTemplateHandler(StepConfig step, out IStepHandler handler);

        /// <summary>
        /// 获取通用回退 Handler（当没有专用 Handler 和模板时使用）。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <returns>回退 Handler 实例。</returns>
        IStepHandler GetFallbackHandler(StepConfig step);
    }
}
