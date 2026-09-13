using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Configuration;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.Services;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine.Evaluation;
using ZL.Gear.Engine.Infrastructure;
using ZL.Gear.Core.Planning;
using ZL.Gear.Engine.Planning;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// SequenceExecutor 构建器 - 提供链式配置 API
    ///
    /// 使用方式：
    /// ```csharp
    /// // 纯逻辑 / 行业扩展（无真实仪器）：必须显式声明 LogicOnly 宿主
    /// using var executor = SequenceExecutorBuilder.Create()
    ///     .AsLogicOnlyDemoHost()
    ///     .WithBuiltInModules(BuiltInModules.Core)
    ///     .WithExtension(new StationExtension())
    ///     .Build();
    ///
    /// // 仪器化产线 / ConsoleApp：显式注入 IDeviceService
    /// var (deviceService, deviceResources) = DriversServiceCollectionExtensions.CreateDeviceService();
    /// using (deviceResources)
    /// using var executor = SequenceExecutorBuilder.Create()
    ///     .AsInstrumentedHost(deviceService)
    ///     .WithBuiltInModules(BuiltInModules.All)
    ///     .WithDeviceConfig("Protocols/devices.json")
    ///     .WithLogger(Console.WriteLine)
    ///     .Build();
    ///
    /// // 自定义所有服务（仪器化）
    /// using var executor = SequenceExecutorBuilder.Create()
    ///     .AsInstrumentedHost(deviceService)
    ///     .WithProfileService(profileService)
    ///     .WithCustomServices(services => { ... })
    ///     .Build();
    /// ```
    /// </summary>
    public class SequenceExecutorBuilder
    {
        private string? _deviceConfigPath;
        private Action<string>? _logger;
        private int _testStepInterval = 100;
        private bool _disposeDeviceService = true;
        private bool _disposeProfileService = true;
        private string? _projectRoot;
        private string? _defaultLibrary;
        private IDeviceService? _customDeviceService;
        private IGearProfileService? _customProfileService;
        private ILibraryService? _customLibraryService;
        private Action<IServiceCollection>? _customServicesConfig;
        private ExecutionScenario _scenario = ExecutionScenario.Production;
        private List<IDisposable> _resourceHolder = new();
        private IResultEvaluator _resultEvaluator;
        private List<(string command, IStepHandler handler)> _handlerRegistrations = new();
        private readonly List<object> _extensionSources = new();
        private bool _enableUnknownCommandWarning = true;
        private int _defaultStepTimeoutMs = 30000;
        private BuiltInModules _builtInModules = BuiltInModules.All;
        private DeviceHostMode _deviceHostMode = DeviceHostMode.Unspecified;
        private bool _strictPlanCompile = true;
        private bool _validateConditionSyntax = true;
        private bool _clearDeviceQuarantineOnRunStart;
        private bool _built;

        public SequenceExecutorBuilder WithEvaluator(IResultEvaluator resultEvaluator)
        {
            _resultEvaluator = resultEvaluator;
            return this;
        }

        /// <summary>
        /// 设置未知命令走通用回退时是否输出诊断警告日志。
        /// </summary>
        /// <param name="enable">是否启用未知命令诊断警告，默认 true。</param>
        public SequenceExecutorBuilder WithEnableUnknownCommandWarning(bool enable)
        {
            _enableUnknownCommandWarning = enable;
            return this;
        }

        /// <summary>
        /// 计划编译 Strict 模式：未注册命令编译失败（产线默认 true，对标 OpenTAP）。
        /// Demo / legacy 动态命令场景可设为 false。
        /// </summary>
        public SequenceExecutorBuilder WithStrictPlanCompile(bool strict = true)
        {
            _strictPlanCompile = strict;
            return this;
        }

        /// <summary>
        /// 是否对 Parameters.Condition 做编译期语法预检（默认 true）。
        /// </summary>
        public SequenceExecutorBuilder WithValidateConditionSyntax(bool validate = true)
        {
            _validateConditionSyntax = validate;
            return this;
        }

        /// <summary>
        /// 每次 Run 开始前清空设备隔离表（默认 false；超时隔离持续到 Release/ClearDeviceQuarantine）。
        /// 复检 / 同工位连续测板场景可设为 true。
        /// </summary>
        public SequenceExecutorBuilder WithClearDeviceQuarantineOnRunStart(bool clear = true)
        {
            _clearDeviceQuarantineOnRunStart = clear;
            return this;
        }

        /// <summary>
        /// 设置步骤未显式配置 TimeoutMs 时的默认超时（毫秒，默认 30000）。
        /// </summary>
        /// <param name="timeoutMs">默认步骤超时（毫秒）；非正值忽略并保留当前默认。</param>
        public SequenceExecutorBuilder WithDefaultStepTimeoutMs(int timeoutMs)
        {
            if (timeoutMs > 0) _defaultStepTimeoutMs = timeoutMs;
            return this;
        }

        /// <summary>
        /// 显式勾选内置模块（docs/138 微动）。默认 <see cref="BuiltInModules.All"/>。
        /// </summary>
        /// <remarks>
        /// 仅逻辑 DSL 宿主可传 <see cref="BuiltInModules.Core"/>，避免挂上 Sensing/PLC/AI。
        /// 不改变 Engine 程序集引用图；只影响运行期注册表内容。
        /// </remarks>
        /// <param name="modules">模块掩码。</param>
        public SequenceExecutorBuilder WithBuiltInModules(BuiltInModules modules)
        {
            _builtInModules = modules;
            return this;
        }

        /// <summary>
        /// 声明为纯逻辑 Demo 宿主（无真实设备）。默认 BuiltInModules 将被强制为 Core。
        /// </summary>
        public SequenceExecutorBuilder AsLogicOnlyDemoHost()
        {
            _deviceHostMode = DeviceHostMode.LogicOnly;
            _builtInModules = BuiltInModules.Core;
            return this;
        }

        /// <summary>
        /// 声明为仪器化产线宿主（须注入真实 IDeviceService，对齐 docs/145）。
        /// </summary>
        /// <param name="deviceService">外部传入的设备服务实例。</param>
        /// <remarks>
        /// **生命周期约定**：由外部传入 <paramref name="deviceService"/>，Builder 不负责 Dispose。
        /// 宿主应自行管理该实例的生命周期（如托管在 DI 容器中）。
        /// </remarks>
        public SequenceExecutorBuilder AsInstrumentedHost(IDeviceService deviceService)
        {
            if (deviceService == null) throw new ArgumentNullException(nameof(deviceService));
            _deviceHostMode = DeviceHostMode.Instrumented;
            _customDeviceService = deviceService;
            _disposeDeviceService = false;
            return this;
        }

        /// <summary>
        /// 注册自定义步骤处理器，由构建器在初始化工作流服务后统一注入。
        /// </summary>
        /// <param name="command">命令名称，对应 StepConfig.Command。</param>
        /// <param name="handler">处理器实例。</param>
        public SequenceExecutorBuilder WithHandlers(string command, IStepHandler handler)
        {
            if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("命令名称不能为空。", nameof(command));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            _handlerRegistrations.Add((command, handler));
            return this;
        }

        /// <summary>
        /// 加载行业扩展（<see cref="IGearExtension"/>），在 Build 时经 <see cref="StepDispatcher.LoadModules"/> 装入。
        /// </summary>
        /// <remarks>
        /// 对齐 docs/138：行业差异走插件缝，不要把业务写进 Engine。
        /// 扩展应使用 <see cref="IStepHandlerRegistry.RegisterHandlerWithAction"/>，确保 DynamicFlow 的 ActionKey 可解析。
        /// </remarks>
        /// <param name="extension">扩展实例。</param>
        public SequenceExecutorBuilder WithExtension(IGearExtension extension)
        {
            if (extension == null) throw new ArgumentNullException(nameof(extension));
            _extensionSources.Add(extension);
            return this;
        }

        /// <summary>
        /// 批量加载行业扩展。
        /// </summary>
        /// <param name="extensions">扩展实例集合。</param>
        public SequenceExecutorBuilder WithExtensions(params IGearExtension[] extensions)
        {
            if (extensions == null) throw new ArgumentNullException(nameof(extensions));
            foreach (var extension in extensions)
            {
                WithExtension(extension);
            }
            return this;
        }

        private SequenceExecutorBuilder() { }

        /// <summary>
        /// 创建构建器实例
        /// </summary>
        public static SequenceExecutorBuilder Create() => new SequenceExecutorBuilder();

        /// <summary>
        /// 设置设备配置文件路径
        /// </summary>
        /// <param name="path">devices.json 文件路径</param>
        public SequenceExecutorBuilder WithDeviceConfig(string path)
        {
            _deviceConfigPath = path;
            return this;
        }

        /// <summary>
        /// 设置项目根目录（用于加载项目库配置）
        /// </summary>
        public SequenceExecutorBuilder WithProjectRoot(string root, string defaultLibrary = null)
        {
            _projectRoot = root;
            _defaultLibrary = defaultLibrary;
            return this;
        }

        /// <summary>
        /// 设置日志输出委托
        /// </summary>
        /// <param name="logger">日志输出函数</param>
        public SequenceExecutorBuilder WithLogger(Action<string> logger)
        {
            _logger = logger;
            return this;
        }

        /// <summary>
        /// 设置测试步骤间隔（毫秒）
        /// </summary>
        public SequenceExecutorBuilder WithTestStepInterval(int intervalMs)
        {
            _testStepInterval = intervalMs;
            return this;
        }

        /// <summary>
        /// 使用自定义的 IDeviceService（构建器不会自动释放）
        /// </summary>
        public SequenceExecutorBuilder WithDeviceService(IDeviceService deviceService)
        {
            _customDeviceService = deviceService;
            _disposeDeviceService = false;
            return this;
        }

        /// <summary>
        /// 使用自定义的 IGearProfileService（构建器不会自动释放）
        /// </summary>
        public SequenceExecutorBuilder WithProfileService(IGearProfileService profileService)
        {
            _customProfileService = profileService;
            _disposeProfileService = false;
            return this;
        }

        /// <summary>
        /// 使用自定义的 ILibraryService（构建器不会自动释放）
        /// </summary>
        public SequenceExecutorBuilder WithLibraryService(ILibraryService libraryService)
        {
            _customLibraryService = libraryService;
            return this;
        }

        /// <summary>
        /// 自定义服务配置（高级用法）
        /// </summary>
        public SequenceExecutorBuilder WithCustomServices(Action<IServiceCollection> configure)
        {
            _customServicesConfig = configure;
            return this;
        }

        /// <summary>
        /// 设置执行场景，影响中间件管道组合
        /// </summary>
        /// <param name="scenario">执行场景</param>
        public SequenceExecutorBuilder WithScenario(ExecutionScenario scenario)
        {
            _scenario = scenario;
            return this;
        }

        /// <summary>
        /// 构建 SequenceExecutor 实例。
        /// 完成设备配置加载、工作流服务初始化、扩展注册，返回可执行的序列执行器。
        /// </summary>
        public SequenceExecutor Build()
        {
            _logger ??= msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
            _logger?.Invoke("[SequenceExecutorBuilder] 开始构建 SequenceExecutor...");

            if (_deviceHostMode == DeviceHostMode.Unspecified)
            {
                throw new InvalidOperationException("未声明宿主：请先调用 AsLogicOnlyDemoHost() 或 AsInstrumentedHost(svc)。");
            }
            if (_deviceHostMode == DeviceHostMode.LogicOnly && !string.IsNullOrEmpty(_deviceConfigPath))
            {
                throw new InvalidOperationException("LogicOnly 模式禁止加载 devices.json。");
            }
            if (_deviceHostMode == DeviceHostMode.LogicOnly && _builtInModules != BuiltInModules.Core)
            {
                throw new InvalidOperationException("LogicOnly Demo 宿主仅允许 BuiltInModules.Core。");
            }
            if (_deviceHostMode == DeviceHostMode.Instrumented && _customDeviceService == null)
            {
                throw new InvalidOperationException("Instrumented 模式必须注入 IDeviceService（WithDeviceService / AsInstrumentedHost）。");
            }

            // 同一 builder 实例不可重复 Build（配置已被首个执行器消费，二次 Build 属误用）
            if (_built)
            {
                throw new InvalidOperationException("同一 SequenceExecutorBuilder 实例不能重复调用 Build()；如需新的执行器，请重新 Create()。");
            }
            _built = true;

            // 1. 初始化项目库服务 (核心上下文)
            var libraryService = _customLibraryService ?? CreateDefaultLibraryService();

            // 2. 加载设备配置
            // 优先级：手动指定路径 > 当前库配置路径
            string? targetDeviceConfig = _deviceConfigPath;
            if (_deviceHostMode != DeviceHostMode.LogicOnly)
            {
                if (string.IsNullOrEmpty(targetDeviceConfig))
                {
                    try { targetDeviceConfig = libraryService.CurrentLibraryConfig?.DevicesPath; }
                    catch { /* 配置获取失败时使用默认值 */ }
                }

                if (!string.IsNullOrEmpty(targetDeviceConfig) && File.Exists(targetDeviceConfig))
                {
                    _logger?.Invoke($"[SequenceExecutorBuilder] 加载设备配置: {targetDeviceConfig}");
                    UnifiedDeviceLoader.Load(targetDeviceConfig);
                }
            }

            // 3. 创建或使用自定义的 IDeviceService
            // Build 门禁已保证：Instrumented 必带 _customDeviceService（否则上方已抛异常），
            // 故走到 ?: 右侧时只可能是 LogicOnly，统一由 fail-closed 的 LogicOnlyDeviceService 兜底。
            var deviceService = _customDeviceService ?? new LogicOnlyDeviceService();

            // 4. 创建或使用自定义的 IGearProfileService
            var profileService = _customProfileService ?? CreateDefaultProfileService(libraryService);

            // 5. 初始化工作流服务并获取服务提供者，用于注册自定义 Handler
            var provider = InitializeWorkflowServices(libraryService);

            // 注册自定义 Handler / 行业扩展（避免调用方通过 ServiceLocator 获取 StepDispatcher）
            // 顺序：先 WithHandlers，后 WithExtension；同名命令后者默认 allowOverwrite 覆盖前者，产线应避免冲突。
            var dispatcher = provider.GetService(typeof(StepDispatcher)) as StepDispatcher;
            if (dispatcher != null)
            {
                foreach (var (command, handler) in _handlerRegistrations)
                {
                    dispatcher.RegisterHandlerWithAction(command, handler);
                    _logger?.Invoke($"[SequenceExecutorBuilder] 已注册自定义 Handler: {command}");
                }

                if (_extensionSources.Count > 0)
                {
                    // IGearExtension.Initialize 内应使用 RegisterHandlerWithAction，保证 DynamicFlow ActionKey 可解析
                    dispatcher.LoadModules(_extensionSources.ToArray());
                    _logger?.Invoke($"[SequenceExecutorBuilder] 已加载扩展模块数: {_extensionSources.Count}");
                }
            }
            else if (_handlerRegistrations.Count > 0 || _extensionSources.Count > 0)
            {
                _logger?.Invoke("[SequenceExecutorBuilder] 警告: 无法获取 StepDispatcher，自定义 Handler/扩展未注册");
            }

            // 6. 创建日志器
            var logger = CreateLogger();

            // 7. 创建 SequenceExecutor
            _logger?.Invoke("[SequenceExecutorBuilder] 构建完成");

            // 创建包装器，负责资源释放
            var eventBus = provider.GetService(typeof(IEventBus)) as IEventBus;

            return new ManagedSequenceExecutor(
                deviceService,
                profileService,
                eventBus,
                logger,
                _testStepInterval,
                _resourceHolder,
                _disposeDeviceService && _customDeviceService == null,
                _disposeProfileService && _customProfileService == null,
                _resultEvaluator ?? ResultEvaluator.Instance,
                provider);
        }

        /// <summary>
        /// 异步构建并直接执行测试
        /// </summary>
        public async Task<TestRunResult> ExecuteAsync(
            List<StepConfig> steps,
            string model,
            string barcode,
            Dictionary<string, object>? globalContext = null,
            CancellationToken token = default)
        {
            using var executor = Build();
            return await executor.ExecuteAsync(
                steps,
                model,
                barcode,
                globalContext ?? new Dictionary<string, object>(),
                token);
        }

        private ILibraryService CreateDefaultLibraryService()
        {
            var service = new LibraryService();
            if (!string.IsNullOrEmpty(_projectRoot))
            {
                _logger?.Invoke($"[SequenceExecutorBuilder] 初始化项目库服务: {_projectRoot}");
                service.Initialize(_projectRoot, _defaultLibrary);
            }
            return service;
        }

        private IGearProfileService CreateDefaultProfileService(ILibraryService libraryService)
        {
            _logger?.Invoke("[SequenceExecutorBuilder] 加载设备角色配置...");
            try
            {
                // 优先尝试使用标准实现
                return new GearProfileServices(libraryService);
            }
            catch
            {
                return new SimpleGearProfileService();
            }
        }

        private IServiceProvider InitializeWorkflowServices(ILibraryService libraryService)
        {
            _logger?.Invoke("[SequenceExecutorBuilder] 初始化工作流服务...");

            var services = new ServiceCollection();

            // 注册日志委托
            services.AddSingleton(_logger ?? (_ => { }));

            // 注册项目库服务
            services.AddSingleton(libraryService);
            services.AddSingleton<IStepCatalogService, StepCatalogService>();

            // 注册工作流评估器
            services.AddSingleton<IWorkflowEvaluator, SimpleWorkflowEvaluator>();

            // 注册动作注册表和解析器
            services.AddSingleton<SimpleActionRegistry>();
            services.AddSingleton<IActionRegistry>(sp => sp.GetRequiredService<SimpleActionRegistry>());
            services.AddSingleton<IActionResolver>(sp => sp.GetRequiredService<SimpleActionRegistry>());

            // 注册场景化管道提供器
            services.AddSingleton<IScenarioPipelineProvider>(sp =>
            {
                var handlerFactory = new DefaultStepHandlerFactory();
                return new DefaultScenarioPipelineProvider(handlerFactory);
            });

            // Handler 查找表与 StepDispatcher 共享同一 RegistryStepHandlerLookup 实例（PlanCompiler 预检可感知模板）
            services.AddSingleton(sp => new RegistryStepHandlerLookup(
                new DefaultStepHandlerFactory(),
                new DefaultStepHandlerProvider()));
            services.AddSingleton<IStepHandlerLookup>(sp => sp.GetRequiredService<RegistryStepHandlerLookup>());
            services.AddSingleton<IRegisterableStepHandlerLookup>(sp => sp.GetRequiredService<RegistryStepHandlerLookup>());

            services.AddSingleton(sp =>
            {
                var actionRegistry = sp.GetRequiredService<IActionRegistry>();
                var log = sp.GetRequiredService<Action<string>>();
                var pipelineProvider = sp.GetRequiredService<IScenarioPipelineProvider>();
                var pipeline = pipelineProvider.GetPipeline(_scenario, log);
                var lookup = sp.GetRequiredService<RegistryStepHandlerLookup>();
                return new StepDispatcher(
                    lookup,
                    new DefaultStepHandlerFactory(),
                    actionRegistry,
                    pipeline,
                    log,
                    _enableUnknownCommandWarning,
                    _defaultStepTimeoutMs,
                    _builtInModules);
            });

            services.AddSingleton<IStepHandlerRegistry>(sp => sp.GetRequiredService<StepDispatcher>());
            services.AddSingleton<IPlanCompiler, DefaultPlanCompiler>();
            services.AddSingleton(new PlanCompileOptions
            {
                StrictMissingHandlerCheck = _strictPlanCompile,
                ValidateConditionSyntax = _validateConditionSyntax
            });
            services.AddSingleton(new SequenceExecutorRuntimeOptions
            {
                ClearDeviceQuarantineOnRunStart = _clearDeviceQuarantineOnRunStart
            });
            services.AddSingleton<IDeviceQuarantineService, RuntimeDeviceQuarantineService>();
            // 每个 Runtime 独立 EventBus，避免多工位串事件（对标 OpenTAP PlanRun 隔离）
            services.AddSingleton<IEventBus, DefaultEventBus>();

            // 自定义服务配置（如果指定）
            _customServicesConfig?.Invoke(services);

            // 每个 Build 产出独立 ServiceProvider，不再写入进程级 WorkflowGlobal（对标多 Runtime 隔离）。
            return services.BuildServiceProvider();
        }

        private ILogger<SequenceExecutor> CreateLogger()
        {
            return new BuilderLogger<SequenceExecutor>(_logger ?? (_ => { }));
        }
    }

    /// <summary>
    /// 托管的 SequenceExecutor - 负责资源释放
    /// </summary>
    internal class ManagedSequenceExecutor : SequenceExecutor, IDisposable
    {
        private readonly List<IDisposable> _resourceHolder;
        private readonly bool _shouldDisposeDeviceService;
        private readonly bool _shouldDisposeProfileService;
        private readonly IDeviceService _deviceService;
        private readonly IGearProfileService _profileService;
        private bool _disposed;

        public ManagedSequenceExecutor(
            IDeviceService deviceService,
            IGearProfileService profileService,
            IEventBus eventBus,
            ILogger<SequenceExecutor> logger,
            int testStepInterval,
            List<IDisposable> resourceHolder,
            bool disposeDeviceService,
            bool disposeProfileService,
            IResultEvaluator resultEvaluator,
            IServiceProvider serviceProvider)
            : base(deviceService, profileService, eventBus, logger, resultEvaluator, testStepInterval, serviceProvider)
        {
            _deviceService = deviceService;
            _profileService = profileService;
            _resourceHolder = resourceHolder;
            _shouldDisposeDeviceService = disposeDeviceService;
            _shouldDisposeProfileService = disposeProfileService;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_shouldDisposeDeviceService && _deviceService is IDisposable disposableDevice)
            {
                try { disposableDevice.Dispose(); } catch { /* Dispose 不应抛出异常 */ }
            }

            if (_shouldDisposeProfileService && _profileService is IDisposable disposableProfile)
            {
                try { disposableProfile.Dispose(); } catch { /* Dispose 不应抛出异常 */ }
            }

            // 释放资源持有者
            foreach (var resource in _resourceHolder)
            {
                try { resource.Dispose(); } catch { /* Dispose 不应抛出异常 */ }
            }
            _resourceHolder.Clear();

            base.Dispose();
        }
    }

    /// <summary>
    /// 构建器专用日志器
    /// </summary>
    internal class BuilderLogger<T> : ILogger<T>
    {
        private readonly Action<string> _log;

        public BuilderLogger(Action<string> log)
        {
            _log = log;
        }

        public IDisposable BeginScope<TState>(TState state) => NullDisposable.Instance;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var prefix = logLevel switch
            {
                Microsoft.Extensions.Logging.LogLevel.Information => "[INFO]",
                Microsoft.Extensions.Logging.LogLevel.Warning => "[WARN]",
                Microsoft.Extensions.Logging.LogLevel.Error => "[ERROR]",
                Microsoft.Extensions.Logging.LogLevel.Debug => "[DEBUG]",
                _ => $"[{logLevel}]"
            };
            _log($"{timestamp} {prefix} {message}");
        }
    }

    /// <summary>
    /// 空Disposable实现
    /// </summary>
    internal class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }

    /// <summary>
    /// 简单的设备配置服务实现 - 支持无配置模式
    /// </summary>
    public class SimpleGearProfileService : IGearProfileService
    {
        private readonly Dictionary<string, object> _deviceRoles;

        public SimpleGearProfileService()
        {
            _deviceRoles = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        public SimpleGearProfileService(Dictionary<string, object> roles)
        {
            _deviceRoles = roles ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, object> LoadDeviceRoles()
        {
            return _deviceRoles;
        }
    }

    /// <summary>
    /// 简单工作流评估器：委托正式 WorkflowEvaluator，避免与 docs/133 方言分叉。
    /// </summary>
    public class SimpleWorkflowEvaluator : IWorkflowEvaluator
    {
        private readonly WorkflowEvaluator _inner = new WorkflowEvaluator();

        public bool EvaluateCondition(string expression, IDictionary<string, object> variables)
            => _inner.EvaluateCondition(expression, variables);

        public object EvaluateValue(object input, IDictionary<string, object> variables)
            => _inner.EvaluateValue(input, variables);

        public object EvaluateExpression(string expression, IDictionary<string, object> variables)
            => _inner.EvaluateExpression(expression, variables);

        public string Interpolate(string template, IDictionary<string, object> variables)
            => _inner.Interpolate(template, variables);

        public bool TryValidateConditionSyntax(string expression, out string errorMessage)
            => _inner.TryValidateConditionSyntax(expression, out errorMessage);
    }

    /// <summary>
    /// 简单的动作注册表实现
    /// </summary>
    public class SimpleActionRegistry : IActionRegistry, IActionResolver
    {
        private readonly Dictionary<string, ActionDelegate> _actions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MeasurementActionDelegate> _measurements = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterAction(string name, Func<StepConfig, StepContext, Task<(bool Success, string Message)>> setupFunc, RegistrationPolicy policy = RegistrationPolicy.ThrowIfExists)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (setupFunc == null) return;

            if (policy == RegistrationPolicy.Ignore && _actions.ContainsKey(name))
            {
                return;
            }

            if (policy == RegistrationPolicy.ThrowIfExists && _actions.ContainsKey(name))
            {
                throw new InvalidOperationException($"动作 '{name}' 已注册。");
            }

            _actions[name] = async (step, ctx) =>
            {
                try
                {
                    var result = await setupFunc(step, ctx);
                    return result.Success ? ExecutionResult.Succeeded(result.Message) : ExecutionResult.Failed(result.Message);
                }
                catch (Exception ex)
                {
                    return ExecutionResult.Failed($"动作 '{name}' 执行异常: {ex.Message}");
                }
            };
        }

        public void RegisterAction(string name, ActionDelegate action, RegistrationPolicy policy = RegistrationPolicy.ThrowIfExists)
        {
            _actions[name] = action;
        }

        public void RegisterMeasurement(string name, MeasurementActionDelegate action, RegistrationPolicy policy = RegistrationPolicy.ThrowIfExists)
        {
            _measurements[name] = action;
        }

        public ActionDelegate ResolveAction(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _actions.TryGetValue(name, out var action) ? action : null;
        }

        public MeasurementActionDelegate ResolveMeasurement(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _measurements.TryGetValue(name, out var measurement) ? measurement : null;
        }

        public IEnumerable<string> GetRegisteredActions()
        {
            return _actions.Keys.ToList();
        }
    }
}
