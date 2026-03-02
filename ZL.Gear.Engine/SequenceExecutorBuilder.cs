using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.Services;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Drivers.Core;
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
        private IDeviceService? _customDeviceService;
        private IGearProfileService? _customProfileService;
        private Action<IServiceCollection>? _customServicesConfig;
        private List<IDisposable> _resourceHolder = new();

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
        /// 自定义服务配置（高级用法）
        /// </summary>
        public SequenceExecutorBuilder WithCustomServices(Action<IServiceCollection> configure)
        {
            _customServicesConfig = configure;
            return this;
        }

        /// <summary>
        /// 构建 SequenceExecutor 实例
        /// </summary>
        public SequenceExecutor Build()
        {
            _logger?.Invoke("[SequenceExecutorBuilder] 开始构建 SequenceExecutor...");

            // 1. 加载设备配置（如果指定了路径）
            if (!string.IsNullOrEmpty(_deviceConfigPath) && File.Exists(_deviceConfigPath))
            {
                _logger?.Invoke($"[SequenceExecutorBuilder] 加载设备配置: {_deviceConfigPath}");
                UnifiedDeviceLoader.Load(_deviceConfigPath);
            }

            // 2. 创建或使用自定义的 IDeviceService
            var deviceService = _customDeviceService ?? CreateDefaultDeviceService();

            // 3. 创建或使用自定义的 IGearProfileService
            var profileService = _customProfileService ?? CreateDefaultProfileService();

            // 4. 初始化工作流服务
            InitializeWorkflowServices();

            // 5. 创建日志器
            var logger = CreateLogger();

            // 6. 创建 SequenceExecutor
            _logger?.Invoke("[SequenceExecutorBuilder] 构建完成");

            // 创建包装器，负责资源释放
            return new ManagedSequenceExecutor(
                deviceService,
                profileService,
                logger,
                _testStepInterval,
                _resourceHolder,
                _disposeDeviceService && _customDeviceService == null,
                _disposeProfileService && _customProfileService == null);
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

        private IGearProfileService CreateDefaultProfileService()
        {
            _logger?.Invoke("[SequenceExecutorBuilder] 加载设备角色配置...");
            return new SimpleGearProfileService();
        }

        private void InitializeWorkflowServices()
        {
            _logger?.Invoke("[SequenceExecutorBuilder] 初始化工作流服务...");

            var services = new ServiceCollection();

            // 注册日志委托
            services.AddSingleton(_logger ?? (_ => { }));

            // 注册工作流评估器
            services.AddSingleton<IWorkflowEvaluator, SimpleWorkflowEvaluator>();

            // 注册动作注册表和解析器
            services.AddSingleton<SimpleActionRegistry>();
            services.AddSingleton<IActionRegistry>(sp => sp.GetRequiredService<SimpleActionRegistry>());
            services.AddSingleton<IActionResolver>(sp => sp.GetRequiredService<SimpleActionRegistry>());

            // 注册 StepDispatcher
            services.AddSingleton(sp =>
            {
                var actionRegistry = sp.GetRequiredService<IActionRegistry>();
                var log = sp.GetRequiredService<Action<string>>();
                return new StepDispatcher(actionRegistry, log);
            });

            // 自定义服务配置（如果指定）
            _customServicesConfig?.Invoke(services);

            var provider = services.BuildServiceProvider();
            WorkflowGlobal.Initialize(provider);
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
            bool disposeProfileService)
            : base(deviceService, profileService, logger, testStepInterval)
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
                try { disposableDevice.Dispose(); } catch { }
            }

            if (_shouldDisposeProfileService && _profileService is IDisposable disposableProfile)
            {
                try { disposableProfile.Dispose(); } catch { }
            }

            // 释放资源持有者
            foreach (var resource in _resourceHolder)
            {
                try { resource.Dispose(); } catch { }
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
    }
}
