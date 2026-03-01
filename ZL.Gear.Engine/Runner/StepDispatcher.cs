using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Engine.Runner.Middlewares;
using ZL.Gear.Sensing.LinkageMeasurement;

namespace ZL.Gear.Engine.Runner
{
    public class StepDispatcher : IStepHandlerRegistry
    {
        private readonly Dictionary<string, IStepHandler> _handlers = new();
        private readonly TemplateFlowHandler _templateHandler;
        private readonly CommonHandler _commonHandler;
        private readonly IActionRegistry _actionService = null;

        private readonly StepExecutionPipeline _pipeline = new();
        private Action<string> _log { get; set; } = s => { };

        public StepDispatcher(IActionRegistry actionService, Action<string> log)
        {
            _actionService = actionService;
            _log = log ?? (s => { });

            // 注册标准动作
            var standardProvider = new StandardActionsProvider();
            standardProvider.RegisterActions(_actionService);
            
            var universalProvider = new UniversalActionProvider();
            universalProvider.RegisterActions(_actionService);

            // 注册核心处理器
            RegisterHandler("DynamicFlow", new DynamicFlowHandler());
            
            // 注册主从联动测量处理器
            RegisterHandler("TriggeredMeasure", new LinkageMeasureHandler(_log));
            
            // 注册 MicroWorkflow 综合演示处理器
            RegisterHandler("MicroWorkflowDemo", new BuiltIn.MicroWorkflowDemoHandler(_log));
            
            // 注册 MicroWorkflow 演示动作
            BuiltIn.MicroWorkflowDemoActions.Register(_actionService, _log);
            
            // 注册通用设备调用处理器（最终回退）
            _commonHandler = new CommonHandler();

            // 注册模板回退处理器
            // 默认搜索当前目录下的 Templates，以及工程根目录下的 docs/Templates
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string templatePath = Path.Combine(baseDir, "Templates");
            if (!Directory.Exists(templatePath))
            {
                // 向上找 3 级尝试定位到源码 root (Debug 模式下)
                var parent = Directory.GetParent(baseDir)?.Parent?.Parent;
                if (parent != null) templatePath = Path.Combine(parent.FullName, "docs", "Templates");
            }
            _templateHandler = new TemplateFlowHandler(templatePath);

            // 注册流水线中间件
            _pipeline.Use(new LoggingMiddleware(_log));
        }

        public void RegisterHandler(string command, IStepHandler handler, bool allowOverwrite = true)
        {
            if (!_handlers.ContainsKey(command) || allowOverwrite)
            {
                _handlers[command] = handler;
            }
            else
            {
                _log($"命令 '{command}' 已经被注册，如需覆盖请设置 allowOverwrite=true");
                throw new InvalidOperationException($"命令 '{command}' 已经被注册，如需覆盖请设置 allowOverwrite=true");
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sources"></param>
        /*
         场景 1：如果你的 Extension.Seat.dll 已经在 Bin 目录下
        （大多数情况，Seat 项目引用了 Engine，编译时自动复制过去了）
        // Program.cs
        static void Main()
        {
            // 1. 初始化基础服务
            LogKit.Initialize();
            WorkflowGlobal.Initialize(WorkflowActionService.Instance);
            StepDispatcher.Initialize(LogKit.Info);

            // 2. 【加载核心业务模块】
            // 使用 LoadModules 统一入口，传入程序集即可
            // 假设 ZL.Gear.Drivers 和 ZL.Gear.Extension.Seat 已经被引用到主工程
            StepDispatcher.Instance.LoadModules(
                Assembly.Load("ZL.Gear.Drivers"), 
                Assembly.Load("ZL.Gear.Extension.Seat") 
            );
    
            // 3. 启动 UI 或 Runner
        }
        场景 2：如果是真正的“插件化” (DLL 在 Plugins 文件夹，未被主工程引用)
        static void Main()
        {
            // ... 初始化 ...

            // 2. 加载外部插件目录
            string pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            StepDispatcher.Instance.LoadModules(pluginPath); // Loader 会自动扫描目录下的 .dll
        }
         */
        public void LoadModules(params object[] sources)
        {
            var loader = new ModuleLoader(this, _actionService, _log);
            loader.Load(sources);
        }

        /// <summary>
        /// [核心改造] 分发并执行单个、原子的测试命令。
        /// 此方法现在只负责处理一个没有子步骤的 StepConfig。
        /// 它不再关心并发、延迟或结果聚合。
        /// </summary>
        /// <param name="step">要执行的步骤配置。</param>
        /// <param name="context">执行上下文，包含设备和父级CancellationToken。</param>
        /// <returns>一个包含测量结果列表的执行结果。</returns>
        public async Task<ExecutionResult<List<Measurement>>> DispatchSingleAsync(StepConfig step, StepContext context)
        {
            return await _pipeline.ExecuteAsync(step, context, DispatchCoreAsync);
        }

        private async Task<ExecutionResult<List<Measurement>>> DispatchCoreAsync(StepConfig step, StepContext context)
        {
            // 1. 查找处理器
            if (string.IsNullOrEmpty(step.Command))
            {
                _log($"{step.StepName} Command 为空，直接返回成功。");
                return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "容器步骤，无命令执行。");
            }

            IStepHandler handler = null;

            // 策略 1: 查找专用 Handler (如 PlcStepHandler)
            if (_handlers.TryGetValue(step.Command, out var registeredHandler))
            {
                handler = registeredHandler;
                _log($"使用专用 Handler: {step.Command}");
            }
            else
            {
                // 策略 2: 尝试 JSON DSL 模板
                var templateResult = await _templateHandler.ExecuteAsync(step, context);
                if (templateResult.Success)
                {
                    // 模板执行成功，转换结果格式
                    if (templateResult is ExecutionResult<List<Measurement>> listResult)
                    {
                        return listResult;
                    }
                    if (templateResult.GetValueAsObject() is Measurement single)
                    {
                        return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement> { single });
                    }
                    return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>());
                }

                // 策略 3: 使用 CommonHandler 通用设备调用（最终回退）
                _log($"未找到专用 Handler 和 DSL 模板，使用通用设备调用: {step.Command}");
                handler = _commonHandler;
            }

            // 2. 设置本步骤的独立超时
            using var stepTimeoutCts = new CancellationTokenSource(step.TimeoutMs > 0 ? step.TimeoutMs : 30000);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, stepTimeoutCts.Token);
            var newContext = context.WithToken(linkedCts.Token);

            // 3. 执行
            try
            {
                var rawResult = await handler.ExecuteAsync(step, newContext);

                // 规范化返回类型
                if (rawResult is ExecutionResult<List<Measurement>> listResult) return listResult;

                if (rawResult.Success)
                {
                    if (rawResult.GetValueAsObject() is Measurement single)
                        return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement> { single });
                    
                    return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), rawResult.SamplesCollected, rawResult.Message);
                }
                
                if (rawResult.GetValueAsObject() is Measurement failSingle)
                    return ExecutionResult<List<Measurement>>.Failed(rawResult.Message, new List<Measurement> { failSingle });

                return ExecutionResult<List<Measurement>>.Failed(rawResult.Message);
            }
            catch (OperationCanceledException)
            {
                return ExecutionResult<List<Measurement>>.Failed("操作超时或被取消。");
            }
            catch (Exception ex)
            {
                // Print stack trace to console for debugging
                Console.WriteLine($"[StepDispatcher] Exception in step {step.StepName}: {ex}");
                return ExecutionResult<List<Measurement>>.Failed($"执行错误: {ex.Message}");
            }
        }

    }
}
