using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine.BuiltIn;
using ZL.Gear.Engine.Handlers;
using ZL.Gear.Engine.Runner;
using ZL.Gear.Sensing.LinkageMeasurement;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// 模块加载器。
    /// 职责：专职负责扫描程序集、加载插件、识别 Bootstrapper，并向注册表注册 Handler 和动作。
    /// 设计要点：
    /// 1. 内置模块通过 <see cref="RegisterBuiltInHandlers"/> 按 <see cref="BuiltInModules"/> 显式勾选
    ///    （AddCore / AddSensing / AddPlc / AddAi）；默认 <see cref="BuiltInModules.All"/> 保持产线行为；
    /// 2. <see cref="StepDispatcher"/> 构造时会调用本方法（可传入模块掩码）；
    /// 3. 支持三种模块源：插件目录路径、Assembly 实例、<see cref="IGearExtension"/> 实例；
    /// 4. 插件支持命名空间隔离（通过 <see cref="PluginManifest.CommandPrefix"/>）；
    /// 5. 优先使用显式 Bootstrapper（IGearExtension），避免隐式扫描导致的重复注册。
    /// </summary>
    internal class ModuleLoader
    {
        /// <summary>
        /// 按「动作注册表」实例记录已注册模块位。
        /// 选用 ActionRegistry 而非 StepDispatcher 作键：同一动作表上重复 RegisterAction 会 ThrowIfExists；
        /// 宿主通常单例 Dispatcher，共享动作表时后建 Dispatcher 若请求已注册过的位将整段跳过（其 Handler 表可能无 DynamicFlow）——产线请保持 Dispatcher 单例。
        /// </summary>
        private static readonly ConditionalWeakTable<object, RegisteredModulesState> _registeredByActionRegistry =
            new ConditionalWeakTable<object, RegisteredModulesState>();

        /// <summary>单个 ActionRegistry 上的已注册掩码（需与注册过程同锁保护）。</summary>
        private sealed class RegisteredModulesState
        {
            public BuiltInModules Mask;
        }

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
        /// 按模块掩码注册框架内置 Handler / 动作（docs/138 微动）。
        /// 默认 <see cref="BuiltInModules.All"/>，与历史「构造即全注册」行为一致。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>幂等键</b>：同一 <paramref name="actionRegistry"/> 上已置位的模块不会再次执行对应 Add*，
        /// 避免 StandardActions 二次 <c>RegisterAction</c> 抛错。
        /// </para>
        /// <para>
        /// <b>并发</b>：计算 pending、执行 Add*、写回 Mask 均在同一把锁内，避免双线程同时 AddCore。
        /// </para>
        /// <para>
        /// <b>增量勾选</b>：可先 <c>Core</c> 再对<b>同一</b> <paramref name="registry"/> + <paramref name="actionRegistry"/>
        /// 调用 <c>Sensing</c>，Handler 会挂到同一 Dispatcher。若第二次 new 了另一个 Dispatcher，
        /// 则新增 Handler 只挂在新实例上（动作仍写入共享 ActionRegistry）。
        /// </para>
        /// </remarks>
        /// <param name="registry">步骤处理器注册表（通常即 StepDispatcher）。</param>
        /// <param name="actionRegistry">动作/测量注册表。</param>
        /// <param name="handlerFactory">Handler 工厂。</param>
        /// <param name="log">日志输出委托。</param>
        /// <param name="modules">要注册的内置模块；默认 All。</param>
        /// <exception cref="ArgumentNullException">任意参数为 null。</exception>
        public static void RegisterBuiltInHandlers(
            IStepHandlerRegistry registry,
            IActionRegistry actionRegistry,
            IStepHandlerFactory handlerFactory,
            Action<string> log,
            BuiltInModules modules = BuiltInModules.All)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (actionRegistry == null) throw new ArgumentNullException(nameof(actionRegistry));
            if (handlerFactory == null) throw new ArgumentNullException(nameof(handlerFactory));
            if (log == null) throw new ArgumentNullException(nameof(log));

            if (modules == BuiltInModules.None)
            {
                log("[ModuleLoader] BuiltInModules.None：跳过内置注册。");
                return;
            }

            var state = _registeredByActionRegistry.GetOrCreateValue(actionRegistry);

            // 必须在锁内完成「算 pending → Add* → 置 Mask」，否则双构造可能并发 AddCore。
            lock (state)
            {
                var pending = modules & ~state.Mask;
                if (pending == BuiltInModules.None)
                {
                    log($"[ModuleLoader] 内置模块已在本 ActionRegistry 注册过（已有={state.Mask}，请求={modules}），跳过。");
                    return;
                }

                log($"[ModuleLoader] 注册内置模块: 请求={modules}，待注册={pending}");

                if ((pending & BuiltInModules.Core) != 0)
                {
                    AddCore(registry, actionRegistry, log);
                }

                if ((pending & BuiltInModules.Sensing) != 0)
                {
                    AddSensing(registry, actionRegistry, log);
                }

                if ((pending & BuiltInModules.Plc) != 0)
                {
                    AddPlc(registry, actionRegistry, handlerFactory, log);
                }

                if ((pending & BuiltInModules.Ai) != 0)
                {
                    AddAi(registry, log);
                }

                state.Mask |= pending;
                log($"[ModuleLoader] 内置模块注册完成（本 ActionRegistry 累计={state.Mask}）");
            }
        }

        /// <summary>
        /// 查询某动作注册表上已注册的内置模块掩码（测试/诊断用）。
        /// </summary>
        public static BuiltInModules GetRegisteredMask(IActionRegistry actionRegistry)
        {
            if (actionRegistry == null) return BuiltInModules.None;
            if (_registeredByActionRegistry.TryGetValue(actionRegistry, out var state))
            {
                lock (state) return state.Mask;
            }

            return BuiltInModules.None;
        }

        /// <summary>
        /// AddCore：逻辑 DSL + DynamicFlow + 演示 Handler。
        /// 不含 GenericMeasure（见 <see cref="AddSensing"/>）。
        /// </summary>
        private static void AddCore(
            IStepHandlerRegistry registry,
            IActionRegistry actionRegistry,
            Action<string> log)
        {
            log("[ModuleLoader] AddCore：StandardActions / DynamicFlow / MicroWorkflowDemo");
            // 标准动作含逻辑子集与设备元语 Write/Read/Query；延迟别名 Delay 使用 Ignore 策略。
            new StandardActionsProvider().RegisterActions(actionRegistry);

            // JSON 微流程主入口（步内 L-DSL）
            var dynamicHandler = new DynamicFlowHandler();
            registry.RegisterHandlerWithAction("DynamicFlow", dynamicHandler);

            // Fluent 综合演示 + Mock ActionKey（非产线配方）
            var microWorkflowDemoHandler = new MicroWorkflowDemoHandler(log);
            registry.RegisterHandlerWithAction("MicroWorkflowDemo", microWorkflowDemoHandler, allowOverwrite: false);
            MicroWorkflowDemoActions.Register(actionRegistry, log);
        }

        /// <summary>
        /// AddSensing：万能测量与主从联动测量（硬依赖 Sensing 类型）。
        /// </summary>
        private static void AddSensing(
            IStepHandlerRegistry registry,
            IActionRegistry actionRegistry,
            Action<string> log)
        {
            log("[ModuleLoader] AddSensing：UniversalActionProvider / TriggeredMeasure");
            new UniversalActionProvider().RegisterActions(actionRegistry);

            var triggeredMeasureHandler = new LinkageMeasureHandler(log);
            registry.RegisterHandlerWithAction("TriggeredMeasure", triggeredMeasureHandler, allowOverwrite: false);
        }

        /// <summary>
        /// AddPlc：仅当 Drivers 已加载时反射注册；失败只打日志，不抛（软依赖）。
        /// </summary>
        private static void AddPlc(
            IStepHandlerRegistry registry,
            IActionRegistry actionRegistry,
            IStepHandlerFactory handlerFactory,
            Action<string> log)
        {
            log("[ModuleLoader] AddPlc：条件注册 PlcStepHandler 命令族");
            RegisterHandlerIfExists(registry, actionRegistry, handlerFactory, log,
                "ZL.Gear.Drivers", "PlcStepHandler",
                "SetupPlcRelay", "StopPlcRelay", "WriteToPlc", "ReadFromPlc", "PlcHandshake", "PlcDelay");
        }

        /// <summary>
        /// AddAi：挂载 AiDecision 命令；策略注入与否在执行期判定。
        /// </summary>
        private static void AddAi(IStepHandlerRegistry registry, Action<string> log)
        {
            log("[ModuleLoader] AddAi：AiDecision");
            var aiHandler = new AiDecisionStepHandler();
            registry.RegisterHandlerWithAction("AiDecision", aiHandler);
        }

        /// <summary>
        /// 条件注册 Handler：如果指定程序集中存在指定类型，则注册其命令映射。
        /// </summary>
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
                        // 显式实例路径：仍须注册同程序集内的 IWorkflowActionProvider（如 Seat WorkflowActionProvider）
                        var extensionTypes = extension.GetType().Assembly
                            .GetTypes()
                            .Where(t => !t.IsInterface && !t.IsAbstract)
                            .ToList();
                        ScanAndRegisterWorkflowActionProvider(extensionTypes);
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

            // 同步清理 StepDispatcher 侧的元数据缓存（attribute/schema），避免热加载后残留旧命令元数据
            if (_registry is StepDispatcher dispatcher)
            {
                dispatcher.RemoveMetadataByPrefix(prefixWithDot);
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
            foreach (var type in types.Where(t =>
                         typeof(IWorkflowActionProvider).IsAssignableFrom(t)
                         && !t.IsInterface
                         && !t.IsAbstract))
            {
                try
                {
                    var provider = _serviceProvider?.GetService(type) as IWorkflowActionProvider
                                   ?? Activator.CreateInstance(type) as IWorkflowActionProvider;
                    if (provider == null)
                    {
                        _log($"[ModuleLoader][ERROR] 注册 ActionProvider 失败: {type.FullName}, 无法实例化");
                        continue;
                    }

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
                    _registry.RegisterHandlerWithAction(cmdName, instance);
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
