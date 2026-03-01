using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Core.Models
{

    public sealed class StepContext : IServiceProvider
    {
        // === 基础属性 ===
        public string StepKey { get; private set; }
        /// <summary>
        /// 当前步骤的配置
        /// </summary>
        public StepConfig StepConfig { get; set; }
        public StepContext ParentContext { get; }
        /// <summary>
        /// 获取父级配置
        /// </summary>
        public StepConfig ParentStepConfig => ParentContext?.StepConfig;
        public int TimeoutMs => StepConfig.TimeoutMs;

        // === 运行时环境 (引用传递，不应被 Clone) ===
        public CancellationToken CancellationToken { get; }
        /// <summary>
        /// 自动测试模式 还是手动测试模式
        /// </summary>
        public RunTestMode RunTestMode { get; set; }

        // === 3. 数据作用域 ===
        /// <summary>
        /// 全局只读上下文（如 Model, Barcode, UserID 等）
        /// </summary>
        public IReadOnlyDictionary<string, object> GlobalContext { get; }

        /// <summary>
        /// 流程动态变量池 (State) - 替代原 SharedData
        /// </summary>
        public ContextVariableStore Variables { get; }
        //public Action<string> Log { get; private set; }
        public Action<string> Log => GetService<Action<string>>() ?? (s => Console.WriteLine($"[Fallback Log] {s}"));

        /// <summary>
        /// 动作解析器
        /// </summary>
        public IActionResolver ActionResolver => GetService<IActionResolver>();

        /// <summary>
        /// 逻辑评估器（逻辑大脑）
        /// </summary>
        public IWorkflowEvaluator Evaluator => GetService<IWorkflowEvaluator>() ?? new WorkflowEvaluator();

        // === 4. 资源与服务 ===
        /// <summary>
        /// 已租用的硬件设备
        ///存放本次测试序列已经租用好的设备
        /// </summary>
        public IReadOnlyDictionary<string, IDevice> ActiveDevices { get; set; }

        /// <summary>
        /// DI 容器提供者
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        // <summary>
        /// 获取当前步骤租用到的目标设备实例。
        /// </summary>
        public IDevice TargetDevice { get; private set; }

        // 构造函数接收所有已经准备好的依赖项
        public StepContext(string stepKey,
            StepConfig stepCfg,
            IReadOnlyDictionary<string, IDevice> activeDevices,
            IServiceProvider serviceProvider,
            CancellationToken token,
            RunTestMode runTestMode,
            ContextVariableStore variables,
            IReadOnlyDictionary<string, object> globalContext = null,
            StepContext parentContext = null,
            Action<string> log = null)
        {
            StepKey = stepKey;
            StepConfig = stepCfg;
            ActiveDevices = activeDevices ?? throw new ArgumentNullException(nameof(activeDevices));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            CancellationToken = token;
            RunTestMode = runTestMode;
            Variables = variables ?? throw new ArgumentNullException(nameof(variables));
            GlobalContext = globalContext ?? new Dictionary<string, object>();
            ParentContext = parentContext;
            //Log = log ?? (s => { });
            //如果传了就用传的(DI模式)，没传就用全局的(单例模式)
        }

        // === 实现 IServiceProvider 接口 ===
        public object GetService(Type serviceType)
        {
            return _serviceProvider.GetService(serviceType);
        }

        // 泛型辅助方法 (需要 using Microsoft.Extensions.DependencyInjection)
        public T GetService<T>()
        {
            return _serviceProvider.GetService<T>();
        }
        /// <summary>
        /// 智能获取变量。查找顺序：
        /// 1. 本步骤参数 (Parameters)
        /// 2. 共享数据 (SharedData)
        /// 3. 全局上下文 (GlobalContext)
        /// </summary>
        public T Get<T>(string key, T defaultValue = default)
        {
            // 1. 查找 Local (StepConfig.Parameters)
            if (StepConfig.Parameters != null && StepConfig.Parameters.TryGetValue(key, out var localObj))
            {
                // 如果是字符串，尝试解析插值
                if (localObj is string str) localObj = ResolveText(str);

                if (TryConvert(localObj, out T val)) return val;
            }

            // 2. 查找 Variables (Runtime)
            if (Variables.TryGet<T>(key, out var sharedVal)) return sharedVal;

            // 3. 查找 Global (Inputs)
            if (GlobalContext.TryGetValue(key, out var globalObj))
            {
                if (TryConvert(globalObj, out T val)) return val;
            }

            return defaultValue;
        }

        /// <summary>
        /// 解析文本中的变量插值 ${...}
        /// </summary>
        public string ResolveText(string text) => Evaluator.Interpolate(text, Variables.AsDictionary());

        /// <summary>
        /// 评估表达式逻辑
        /// </summary>
        public bool Evaluate(string expression) => Evaluator.EvaluateCondition(expression, Variables.AsDictionary());

        /// <summary>
        /// 评估动态值 @...
        /// </summary>
        public object EvaluateValue(object input) => Evaluator.EvaluateValue(input, Variables.AsDictionary());

        internal void SetTargetDevice(IDevice device) => TargetDevice = device;

        /// <summary>
        /// 提供类型安全的 Get 方法
        /// </summary>
        /// <typeparam name="TDevice"></typeparam>
        /// <param name="deviceKey"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="InvalidCastException"></exception>
        public TDevice GetDevice<TDevice>(string deviceKey) where TDevice : class, IDevice
        {
            if (!ActiveDevices.TryGetValue(deviceKey, out var device))
            {
                throw new InvalidOperationException($"设备 '{deviceKey}' 未在测试开始时被租用，无法在步骤中使用。");
            }

            if (device is TDevice typedDevice) return typedDevice;

            throw new InvalidCastException($"已租用的设备 '{deviceKey}' 的类型为 '{device.GetType().Name}'，与请求的类型 '{typeof(TDevice).Name}' 不匹配。");
        }
        private bool TryConvert<T>(object obj, out T result)
        {
            if (obj == null) { result = default; return false; }
            if (obj is T tVal) { result = tVal; return true; }

            if (obj is Newtonsoft.Json.Linq.JToken token)
            {
                try { result = token.ToObject<T>(); return true; } catch { }
            }

            if (typeof(IConvertible).IsAssignableFrom(typeof(T)))
            {
                try { result = (T)Convert.ChangeType(obj, typeof(T)); return true; } catch { }
            }
            result = default;
            return false;
        }
        /// <summary>
        /// 创建子上下文（替代 DeepClone）。
        /// 用于递归执行子步骤时，隔离配置但共享资源。
        /// </summary>
        public StepContext CreateChildContext(StepConfig childConfig, ContextVariableStore customVariables = null)
        {
            return new StepContext(
                childConfig.StepKey,
                childConfig,
                this.ActiveDevices,
                this._serviceProvider,
                this.CancellationToken,
                this.RunTestMode,
                customVariables ?? this.Variables, // 允许覆盖变量存储
                this.GlobalContext,
                this,
                this.Log
            );
        }
        public StepContext WithToken(CancellationToken newToken)
        {
            return new StepContext(this.StepKey, this.StepConfig, this.ActiveDevices, this._serviceProvider, newToken,
                this.RunTestMode, this.Variables, GlobalContext, this.ParentContext, this.Log);
        }

    }
}
