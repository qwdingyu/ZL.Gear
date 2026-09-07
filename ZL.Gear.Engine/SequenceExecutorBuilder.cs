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
using ZL.Gear.Drivers.Core;
using ZL.Gear.Engine.Evaluation;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// SequenceExecutor 构建器 - 提供链式配置 API
    ///
    /// 使用方式：
    /// ```csharp
    /// // 最小模式：一行初始化
    /// using var executor = SequenceExecutorBuilder.Create()
    ///     .Build();
    ///
    /// // 标准模式：带设备配置
    /// using var executor = SequenceExecutorBuilder.Create()
    ///     .WithDeviceConfig("Protocols/devices.json")
    ///     .WithLogger(Console.WriteLine)
    ///     .Build();
    ///
    /// // 完整模式：自定义所有服务
    /// using var executor = SequenceExecutorBuilder.Create()
    ///     .WithDeviceService(deviceService)
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

        public SequenceExecutorBuilder WithEvaluator(IResultEvaluator resultEvaluator)
        {
            _resultEvaluator = resultEvaluator;
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
        /// 构建 SequenceExecutor 实例
        /// </summary>
        public SequenceExecutor Build()
        {
            _logger?.Invoke("[SequenceExecutorBuilder] 开始构建 SequenceExecutor...");

            // 1. 初始化项目库服务 (核心上下文)
            var libraryService = _customLibraryService ?? CreateDefaultLibraryService();

            // 2. 加载设备配置
            // 优先级：手动指定路径 > 当前库配置路径
            string? targetDeviceConfig = _deviceConfigPath;
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

            // 3. 创建或使用自定义的 IDeviceService
            var deviceService = _customDeviceService ?? CreateDefaultDeviceService();

            // 4. 创建或使用自定义的 IGearProfileService
            var profileService = _customProfileService ?? CreateDefaultProfileService(libraryService);

            // 5. 初始化工作流服务并获取服务提供者，用于注册自定义 Handler
            var provider = InitializeWorkflowServices(libraryService);

            // 注册自定义 Handler（避免调用方通过 ServiceLocator 获取 StepDispatcher）
            if (_handlerRegistrations.Count > 0)
            {
                var dispatcher = provider.GetService(typeof(StepDispatcher)) as StepDispatcher;
                if (dispatcher != null)
                {
                    foreach (var (command, handler) in _handlerRegistrations)
                    {
                        dispatcher.RegisterHandler(command, handler);
                        _logger?.Invoke($"[SequenceExecutorBuilder] 已注册自定义 Handler: {command}");
                    }
                }
                else
                {
                    _logger?.Invoke("[SequenceExecutorBuilder] 警告: 无法获取 StepDispatcher，自定义 Handler 未注册");
                }
            }

            // 6. 创建日志器
            var logger = CreateLogger();

            // 7. 创建 SequenceExecutor
            _logger?.Invoke("[SequenceExecutorBuilder] 构建完成");

            // 创建包装器，负责资源释放
            return new ManagedSequenceExecutor(
                deviceService,
                profileService,
                logger,
                _testStepInterval,
                _resourceHolder,
                _disposeDeviceService && _customDeviceService == null,
                _disposeProfileService && _customProfileService == null,
                _resultEvaluator ?? ResultEvaluator.Instance);
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

        private IDeviceService CreateDefaultDeviceService()
        {
            _logger?.Invoke("[SequenceExecutorBuilder] 创建设备服务...");
            var factory = new UnifiedDeviceFactory();
            _resourceHolder.Add(factory);
            var deviceService = new UnifiedDeviceService(factory);
            return deviceService;
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

            // 注册 StepDispatcher
            // 关键点：RegistryStepHandlerLookup 现在通过 DefaultStepHandlerProvider 外部化模板/回退策略
            // 这样即使后续替换 DSL 引擎或回退逻辑，也不影响 StepDispatcher 构造函数签名
            services.AddSingleton(sp =>
            {
                var actionRegistry = sp.GetRequiredService<IActionRegistry>();
                var log = sp.GetRequiredService<Action<string>>();
                var pipelineProvider = sp.GetRequiredService<IScenarioPipelineProvider>();
                var pipeline = pipelineProvider.GetPipeline(_scenario, log);
                return new StepDispatcher(
                    new RegistryStepHandlerLookup(
                        new DefaultStepHandlerFactory(),
                        new DefaultStepHandlerProvider()),
                    new DefaultStepHandlerFactory(),
                    actionRegistry,
                    pipeline,
                    log);
            });

            // 暴露 IStepHandlerRegistry 接口（由 StepDispatcher 实现），
            // 供运行时 EvaluateResult 命令级元数据查询（SequenceExecutor 桥接）与宿主扩展使用
            services.AddSingleton<IStepHandlerRegistry>(sp => sp.GetRequiredService<StepDispatcher>());

            // 自定义服务配置（如果指定）
            _customServicesConfig?.Invoke(services);

            var provider = services.BuildServiceProvider();
            WorkflowGlobal.Initialize(provider);
            return provider;
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
            ILogger<SequenceExecutor> logger,
            int testStepInterval,
            List<IDisposable> resourceHolder,
            bool disposeDeviceService,
            bool disposeProfileService,
            IResultEvaluator resultEvaluator)
            : base(deviceService, profileService, null, logger, resultEvaluator, testStepInterval)
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
    /// 简单的工作流评估器 - 默认实现
    /// </summary>
    public class SimpleWorkflowEvaluator : IWorkflowEvaluator
    {
        public bool EvaluateCondition(string expression, IDictionary<string, object> variables)
        {
            return true;
        }

        public object EvaluateValue(object input, IDictionary<string, object> variables)
        {
            return input;
        }

        public string Interpolate(string template, IDictionary<string, object> variables)
        {
            if (string.IsNullOrEmpty(template)) return template;

            var result = template;
            foreach (var kvp in variables)
            {
                result = result.Replace($"${{{kvp.Key}}}", kvp.Value?.ToString() ?? "");
            }
            return result;
        }
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
