using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine.BuiltIn;
using ZL.Gear.Engine.Handlers;
using ZL.Gear.Drivers.Plc.Handlers;
using ZL.Gear.Sensing.LinkageMeasurement;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// 模块加载器。
    /// 职责：专职负责扫描程序集、加载插件、识别 Bootstrapper，并向注册表注册 Handler 和动作。
    /// 设计要点：
    /// 1. 内置 Handler 注册逻辑已提取为 <see cref="RegisterBuiltInHandlers"/> 静态方法，
    ///    由 <see cref="StepDispatcher"/> 或宿主在 Composition Root 处显式调用；
    /// 2. 支持三种模块源：插件目录路径、Assembly 实例、<see cref="IGearExtension"/> 实例；
    /// 3. 插件支持命名空间隔离（通过 <see cref="PluginManifest.CommandPrefix"/>）；
    /// 4. 优先使用显式 Bootstrapper（IGearExtension），避免隐式扫描导致的重复注册。
    /// </summary>
    internal class ModuleLoader
    {
        /// <summary>
        /// 步骤处理器注册表。
        /// </summary>
        private readonly IStepHandlerRegistry _registry;

        /// <summary>
        /// 动作注册表。
        /// </summary>
        private readonly IActionRegistry _actionRegistry;

        /// <summary>
        /// Handler 工厂：用于创建插件目录扫描到的 Handler 实例。
        /// </summary>
        private readonly IStepHandlerFactory _handlerFactory;

        /// <summary>
        /// 服务提供者：用于 DI 模式下的扩展解析。
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 日志输出委托。
        /// </summary>
        private readonly Action<string> _log;

        /// <summary>
        /// 内置 Handler 是否已注册（避免重复注册）。
        /// </summary>
        private static int _builtInRegistered;

        /// <summary>
        /// 初始化模块加载器。
        /// </summary>
        /// <param name="registry">步骤处理器注册表。</param>
        /// <param name="actionRegistry">动作注册表。</param>
        /// <param name="handlerFactory">Handler 工厂。</param>
        /// <param name="log">日志输出委托。</param>
        /// <param name="serviceProvider">服务提供者，为 null 时使用反射模式。</param>
        public ModuleLoader(
            IStepHandlerRegistry registry,
            IActionRegistry actionRegistry,
            IStepHandlerFactory handlerFactory,
            Action<string> log,
            IServiceProvider serviceProvider = null)
        {
            _registry = registry;
            _actionRegistry = actionRegistry;
            _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
            _serviceProvider = serviceProvider;
            _log = log;
        }

        /// <summary>
        /// 注册框架内置的通用 Handler 与动作。
        /// 关键点：此方法不再由 <see cref="StepDispatcher"/> 构造函数隐式调用，
        /// 而是由 Composition Root 显式调用，确保注册顺序可控。
        /// </summary>
        /// <param name="registry">步骤处理器注册表。</param>
        /// <param name="actionRegistry">动作注册表。</param>
        /// <param name="handlerFactory">Handler 工厂。</param>
        /// <param name="log">日志输出委托。</param>
        /// <exception cref="ArgumentNullException">任意参数为 null。</exception>
        public static void RegisterBuiltInHandlers(
            IStepHandlerRegistry registry,
            IActionRegistry actionRegistry,
            IStepHandlerFactory handlerFactory,
            Action<string> log)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (actionRegistry == null) throw new ArgumentNullException(nameof(actionRegistry));
            if (handlerFactory == null) throw new ArgumentNullException(nameof(handlerFactory));
            if (log == null) throw new ArgumentNullException(nameof(log));

            // 避免重复注册：即使 StepDispatcher 被多次创建，内置 Handler 也仅注册一次。
            if (System.Threading.Interlocked.CompareExchange(ref _builtInRegistered, 1, 0) == 1)
            {
                log("[ModuleLoader] 内置 Handler 已注册，跳过重复初始化。");
                return;
            }

            log("[ModuleLoader] 注册内置 Handler...");

            // 标准动作（如 Start/Stop/Reset 等通用操作）
            var standardProvider = new StandardActionsProvider();
            standardProvider.RegisterActions(actionRegistry);

            // 通用动作（如日志、延迟、变量读写等）
            var universalProvider = new UniversalActionProvider();
            universalProvider.RegisterActions(actionRegistry);

            // 核心处理器：DynamicFlow（JSON DSL 解释器）
            var dynamicHandler = new DynamicFlowHandler();
            registry.RegisterHandlerWithAction("DynamicFlow", dynamicHandler);

            // PLC 通用操作（所有项目都可使用，如果找到 ZL.Gear.Drivers 程序集）
            RegisterHandlerIfExists(registry, actionRegistry, handlerFactory, log,
                "ZL.Gear.Drivers", "PlcStepHandler",
                "SetupPlcRelay", "StopPlcRelay", "WriteToPlc", "ReadFromPlc", "PlcDelay");

            // 主从联动测量处理器
            var triggeredMeasureHandler = new LinkageMeasureHandler(log);
            registry.RegisterHandlerWithAction("TriggeredMeasure", triggeredMeasureHandler, allowOverwrite: false);

            // MicroWorkflow 综合演示处理器
            var microWorkflowDemoHandler = new MicroWorkflowDemoHandler(log);
            registry.RegisterHandlerWithAction("MicroWorkflowDemo", microWorkflowDemoHandler, allowOverwrite: false);
            MicroWorkflowDemoActions.Register(actionRegistry, log);

            // AI 决策处理器
            var aiHandler = new AiDecisionStepHandler();
            registry.RegisterHandlerWithAction("AiDecision", aiHandler);

            log("[ModuleLoader] 内置 Handler 注册完成");
        }

        /// <summary>
        /// 条件注册 Handler：如果指定程序集中存在指定类型，则注册其命令映射。
        /// </summary>
        /// <param name="registry">注册表。</param>
        /// <param name="actionRegistry">动作注册表。</param>
        /// <param name="handlerFactory">Handler 工厂。</param>
        /// <param name="log">日志输出委托。</param>
        /// <param name="assemblyName">程序集名称。</param>
        /// <param name="className">类型名称。</param>
        /// <param name="commandNames">要注册的命令名称列表。</param>
        private static void RegisterHandlerIfExists(
            IStepHandlerRegistry registry,
            IActionRegistry actionRegistry,
            IStepHandlerFactory handlerFactory,
            Action<string> log,
            string assemblyName,
            string className,
            params string[] commandNames)
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == assemblyName);

                if (assembly == null)
                {
                    log($"[ModuleLoader] 未找到程序集: {assemblyName}");
                    return;
                }

                var type = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == className && typeof(IStepHandler).IsAssignableFrom(t));

                if (type == null)
                {
                    log($"[ModuleLoader] 未找到类型: {assemblyName}.{className}");
                    return;
                }

                var handler = handlerFactory.CreateHandler(type);
                if (handler == null)
                {
                    log($"[ModuleLoader] 无法创建实例: {assemblyName}.{className}");
                    return;
                }

                foreach (var cmd in commandNames)
                {
                    registry.RegisterHandlerWithAction(cmd, handler, allowOverwrite: false);
                    log($"[ModuleLoader] 已注册内置 Handler: {cmd} -> {className}");
                }
            }
            catch (Exception ex)
            {
                log($"[ModuleLoader][ERROR] 注册 Handler 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载模块源。
        /// 支持：插件目录路径、Assembly 实例、<see cref="IGearExtension"/> 实例。
        /// </summary>
        /// <param name="sources">模块源数组。</param>
        public void Load(object[] sources)
        {
            if (sources == null) return;

            foreach (var source in sources)
            {
                switch (source)
                {
                    case string path when Directory.Exists(path):
                        LoadPluginDirectory(path);
                        break;
                    case Assembly assembly:
                        LoadAssembly(assembly);
                        break;
                    case IGearExtension extension:
                        extension.Initialize(_registry);
                        break;
                }
            }
        }

        /// <summary>
        /// 热加载插件目录（不重启主程序）。
        /// 先卸载同名前缀的旧插件，再重新加载，避免重复注册。
        /// </summary>
        /// <param name="pluginDirectory">插件目录。</param>
        public void ReloadPlugin(string pluginDirectory)
        {
            if (string.IsNullOrEmpty(pluginDirectory) || !Directory.Exists(pluginDirectory)) return;

            // 读取插件清单，获取命令前缀
            var manifest = PluginManifestLoader.Load(pluginDirectory);
            var commandPrefix = manifest?.CommandPrefix;

            // 如果存在命令前缀，先卸载旧插件并释放资源
            if (!string.IsNullOrEmpty(commandPrefix))
            {
                UnloadPlugin(commandPrefix);
            }

            _log($"[ModuleLoader] 热加载插件: {pluginDirectory}");
            LoadPluginDirectory(pluginDirectory);
        }

        /// <summary>
        /// 卸载指定命令前缀的插件。
        /// 从注册表中移除所有以 <paramref name="commandPrefix"/> 开头的命令，
        /// 并对实现了 <see cref="IDisposable"/> 或 <see cref="IAsyncDisposable"/> 的 Handler 执行释放。
        /// </summary>
        /// <param name="commandPrefix">插件命令前缀（如 "MyPlugin"）。</param>
        /// <returns>实际卸载的 Handler 数量。</returns>
        public int UnloadPlugin(string commandPrefix)
        {
            if (string.IsNullOrEmpty(commandPrefix))
                throw new ArgumentNullException(nameof(commandPrefix));

            var prefixWithDot = commandPrefix.EndsWith(".") ? commandPrefix : commandPrefix + ".";
            int unloadedCount = 0;
            var removedHandlers = new List<IStepHandler>();

            // 如果注册表支持按前缀卸载并返回实例，则批量卸载
            if (_registry is IRegisterableStepHandlerLookup registrableLookup)
            {
                unloadedCount = registrableLookup.RemoveByPrefix(prefixWithDot, out removedHandlers);
            }

            // 对已移除的 Handler 执行资源释放（优先异步释放）
            foreach (var handler in removedHandlers)
            {
                try
                {
                    if (handler is IAsyncDisposable asyncDisposable)
                    {
                        asyncDisposable.DisposeAsync().GetAwaiter().GetResult();
                    }
                    else if (handler is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _log($"[ModuleLoader] 释放 Handler 资源失败: {handler.GetType().FullName}, {ex.Message}");
                }
            }

            _log($"[ModuleLoader] 已卸载插件前缀: {commandPrefix}, 共 {unloadedCount} 个命令");
            return unloadedCount;
        }

        /// <summary>
        /// 加载插件目录（支持命名空间隔离）。
        /// </summary>
        /// <param name="pluginDirectory">插件目录。</param>
        private void LoadPluginDirectory(string pluginDirectory)
        {
            var manifest = PluginManifestLoader.Load(pluginDirectory);
            if (manifest != null)
            {
                _log($"[ModuleLoader] 发现插件清单: {manifest.Name} v{manifest.Version}");
            }

            var dlls = Directory.GetFiles(pluginDirectory, "*.dll");
            foreach (var dll in dlls)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    LoadPluginAssembly(assembly, manifest);
                }
                catch (Exception ex)
                {
                    _log($"[ModuleLoader][ERROR] 加载插件DLL失败: {dll}, {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 加载插件程序集（支持命名空间隔离）。
        /// </summary>
        /// <param name="assembly">程序集。</param>
        /// <param name="manifest">插件清单。</param>
        private void LoadPluginAssembly(Assembly assembly, PluginManifest manifest)
        {
            var types = assembly.GetTypes().Where(t => !t.IsInterface && !t.IsAbstract).ToList();

            // 扫描 ActionProviders（MicroWorkflow 原子动作级别）
            ScanAndRegisterWorkflowActionProvider(types);

            // 策略 A: 优先寻找显式 Bootstrapper (IGearExtension)
            // 如果找到了 Bootstrapper，就只运行它，不再进行隐式扫描（避免重复注册）
            var bootstrappers = types.Where(t => typeof(IGearExtension).IsAssignableFrom(t)).ToList();
            if (bootstrappers.Any())
            {
                foreach (var bType in bootstrappers)
                {
                    InitializeBootstrapper(bType);
                }
                return;
            }

            // 策略 B: 如果没有 GearExtension，则退化为“隐式自动扫描”模式（兼容旧代码）
            ScanAndRegisterPluginHandler(types, manifest);
        }

        /// <summary>
        /// 扫描并注册插件 Handler（支持命名空间隔离）。
        /// </summary>
        /// <param name="types">类型列表。</param>
        /// <param name="manifest">插件清单。</param>
        private void ScanAndRegisterPluginHandler(List<Type> types, PluginManifest manifest)
        {
            // 扫描所有实现 IStepHandler 的非抽象类型
            foreach (var type in types.Where(t => typeof(IStepHandler).IsAssignableFrom(t)))
            {
                // 约定：类名去掉 Handler 后缀作为命令名
                string cmdName = type.Name.EndsWith("Handler") ? type.Name.Substring(0, type.Name.Length - 7) : type.Name;

                // 如果插件有命令前缀，添加前缀实现命名空间隔离
                if (manifest != null && !string.IsNullOrEmpty(manifest.CommandPrefix))
                {
                    cmdName = $"{manifest.CommandPrefix}.{cmdName}";
                }

                try
                {
                    var instance = _handlerFactory.CreateHandler(type);
                    _registry.RegisterHandlerWithAction(cmdName, instance, allowOverwrite: false);
                }
                catch (Exception ex)
                {
                    _log($"[ModuleLoader][ERROR] 注册插件 Handler 失败: {type.FullName}, {ex.Message}");
                }
            }

            // 约定式扫描：支持 StepHandlerCommandAttribute
            foreach (var type in types.Where(t => t.GetCustomAttributes(typeof(StepHandlerCommandAttribute), false).Length > 0))
            {
                var attr = (StepHandlerCommandAttribute)type.GetCustomAttributes(typeof(StepHandlerCommandAttribute), false)[0];
                try
                {
                    var instance = _handlerFactory.CreateHandler(type);
                    string command = attr.Command;

                    // 如果插件有命令前缀，添加前缀
                    if (manifest != null && !string.IsNullOrEmpty(manifest.CommandPrefix))
                    {
                        command = $"{manifest.CommandPrefix}.{command}";
                    }

                    _registry.RegisterHandlerWithAction(command, instance, attr.AllowOverwrite);

                    // 注册别名（兼容旧命令名或多入口调用）
                    if (attr.Aliases != null && attr.Aliases.Length > 0)
                    {
                        foreach (var alias in attr.Aliases)
                        {
                            if (string.IsNullOrWhiteSpace(alias)) continue;
                            _registry.RegisterHandlerWithAction(alias, instance, attr.AllowOverwrite);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log($"[ModuleLoader][ERROR] 注册约定式 Handler 失败: {type.FullName}, {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 初始化 Bootstrapper（支持 DI 和 IDiGearExtension）。
        /// </summary>
        /// <param name="bType">Bootstrapper 类型。</param>
        private void InitializeBootstrapper(Type bType)
        {
            IGearExtension bootstrapper;
            if (_serviceProvider != null)
            {
                // DI 模式：从 IServiceProvider 解析
                bootstrapper = _serviceProvider.GetService(bType) as IGearExtension;
                if (bootstrapper == null)
                {
                    _log($"[ModuleLoader] 无法从 IServiceProvider 解析扩展: {bType.FullName}");
                    return;
                }
            }
            else
            {
                // 反射模式：通过工厂创建
                bootstrapper = (IGearExtension)_handlerFactory.CreateHandler(bType);
            }

            // 优先调用带 IServiceProvider 的初始化方法
            if (bootstrapper is IDiGearExtension diExtension && _serviceProvider != null)
            {
                diExtension.Initialize(_registry, _serviceProvider);
            }
            else
            {
                bootstrapper.Initialize(_registry);
            }
        }

        /// <summary>
        /// 加载程序集。
        /// </summary>
        /// <param name="assembly">要加载的程序集。</param>
        private void LoadAssembly(Assembly assembly)
        {
            var types = assembly.GetTypes().Where(t => !t.IsInterface && !t.IsAbstract).ToList();
            ScanAndRegisterWorkflowActionProvider(types);

            // 策略 A: 优先寻找显式 Bootstrapper (IGearExtension)
            var bootstrappers = types.Where(t => typeof(IGearExtension).IsAssignableFrom(t)).ToList();
            if (bootstrappers.Any())
            {
                foreach (var bType in bootstrappers)
                {
                    InitializeBootstrapper(bType);
                }
                return; // 关键策略：有显式入口，就只用显式入口
            }

            // 策略 B: 如果没有 GearExtension，则退化为“隐式自动扫描”模式（兼容旧代码）
            ScanAndRegisterHandler(types);
        }

        /// <summary>
        /// 扫描并注册工作流动作提供者（MicroWorkflow 原子动作级别）。
        /// </summary>
        /// <param name="types">类型列表。</param>
        private void ScanAndRegisterWorkflowActionProvider(List<Type> types)
        {
            foreach (var type in types.Where(t => typeof(IWorkflowActionProvider).IsAssignableFrom(t)))
            {
                try
                {
                    var provider = (IWorkflowActionProvider)_handlerFactory.CreateHandler(type);
                    provider.RegisterActions(_actionRegistry);
                }
                catch (Exception ex)
                {
                    _log($"[ModuleLoader][ERROR] 注册 ActionProvider 失败: {type.FullName}, {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 扫描并注册 Handler（隐式自动扫描模式，兼容旧代码）。
        /// </summary>
        /// <param name="types">类型列表。</param>
        private void ScanAndRegisterHandler(List<Type> types)
        {
            // 扫描 Handlers
            foreach (var type in types.Where(t => typeof(IStepHandler).IsAssignableFrom(t)))
            {
                var cmdName = type.Name.EndsWith("Handler") ? type.Name.Substring(0, type.Name.Length - 7) : type.Name;
                try
                {
                    var instance = _handlerFactory.CreateHandler(type);
                    _registry.RegisterHandler(cmdName, instance);
                    _actionRegistry.RegisterAction(cmdName, instance.ExecuteAsync, RegistrationPolicy.ThrowIfExists);
                }
                catch (Exception ex)
                {
                    _log($"[ModuleLoader][ERROR] 注册 Handler 失败: {type.FullName}, {ex.Message}");
                }
            }

            // 约定式扫描：支持 StepHandlerCommandAttribute
            foreach (var type in types.Where(t => t.GetCustomAttributes(typeof(StepHandlerCommandAttribute), false).Length > 0))
            {
                var attr = (StepHandlerCommandAttribute)type.GetCustomAttributes(typeof(StepHandlerCommandAttribute), false)[0];
                try
                {
                    var instance = _handlerFactory.CreateHandler(type);
                    _registry.RegisterHandlerWithAction(attr.Command, instance, attr.AllowOverwrite);

                    // 注册别名（兼容旧命令名或多入口调用）
                    if (attr.Aliases != null && attr.Aliases.Length > 0)
                    {
                        foreach (var alias in attr.Aliases)
                        {
                            if (string.IsNullOrWhiteSpace(alias)) continue;
                            _registry.RegisterHandlerWithAction(alias, instance, attr.AllowOverwrite);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log($"[ModuleLoader][ERROR] 注册约定式 Handler 失败: {type.FullName}, {ex.Message}");
                }
            }
        }
    }
}
