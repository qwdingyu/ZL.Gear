using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Engine.Runner.Middlewares;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 步骤分发器。
    /// 职责：接收 <see cref="StepConfig"/>，通过中间件管道执行，并将结果规范化返回。
    /// 设计要点：
    /// 1. 依赖 <see cref="IStepHandlerLookup"/> 决定使用哪个 Handler；
    /// 2. 依赖 <see cref="IStepHandlerFactory"/> 创建 Handler 实例；
    /// 3. 依赖 <see cref="StepExecutionPipeline"/> 执行中间件链；
    /// 4. 内置 Handler 注册逻辑已迁移至 <see cref="ZL.Gear.Engine.ModuleLoader.RegisterBuiltInHandlers"/>，
    ///    避免构造函数膨胀，符合单一职责原则。
    /// </summary>
    public class StepDispatcher : IStepHandlerRegistry
    {
        /// <summary>
        /// Handler 查找策略：决定使用哪个 Handler（注册表/模板/回退）。
        /// </summary>
        private readonly IStepHandlerLookup _handlerLookup;

        /// <summary>
        /// Handler 工厂：负责创建 Handler 实例。
        /// </summary>
        private readonly IStepHandlerFactory _handlerFactory;

        /// <summary>
        /// 动作注册表：提供原子动作（ActionDelegate）的解析能力。
        /// </summary>
        private readonly IActionRegistry _actionRegistry;

        /// <summary>
        /// 中间件执行管道：包装 Handler 执行，插入日志、审计、熔断等横切逻辑。
        /// </summary>
        private readonly StepExecutionPipeline _pipeline;

        /// <summary>
        /// 日志输出委托。
        /// </summary>
        private Action<string> _log;

        /// <summary>
        /// 未知命令走通用回退时是否输出诊断警告日志。
        /// </summary>
        private readonly bool _enableUnknownCommandWarning;

        /// <summary>
        /// 步骤未显式配置 TimeoutMs 时的默认超时（毫秒）。
        /// </summary>
        private readonly int _defaultTimeoutMs;

        /// <summary>
        /// 命令 -> StepHandlerCommandAttribute 元数据缓存（用于 EvaluateResult 桥接）。
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, StepHandlerCommandAttribute> _handlerMetadata = new();

        /// <summary>
        /// 命令 -> 参数 schema 描述缓存（用于启动期校验与文档生成）。
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _parameterSchemas = new();

        /// <summary>
        /// 兼容旧代码的构造函数。
        /// 使用默认的 <see cref="RegistryStepHandlerLookup"/> 和 <see cref="DefaultStepHandlerFactory"/>，
        /// 并自动注册所有内置 Handler。
        /// </summary>
        /// <param name="actionService">动作注册表。</param>
        /// <param name="log">日志输出委托。</param>
        /// <param name="enableUnknownCommandWarning">未知命令走通用回退时是否输出诊断警告日志，默认 true。</param>
        /// <param name="defaultTimeoutMs">步骤未显式配置 TimeoutMs 时的默认超时（毫秒），默认 30000；非正值回退默认。</param>
        public StepDispatcher(IActionRegistry actionService, Action<string> log, bool enableUnknownCommandWarning = true, int defaultTimeoutMs = 30000)
            : this(
                CreateDefaultLookup(log),
                new DefaultStepHandlerFactory(),
                actionService,
                new StepPipelineBuilder().UseDefaultPipeline().Build(),
                log,
                enableUnknownCommandWarning,
                defaultTimeoutMs)
        {
        }

        /// <summary>
        /// 新构造函数，支持依赖注入。
        /// </summary>
        /// <param name="handlerLookup">Handler 查找策略。</param>
        /// <param name="handlerFactory">Handler 工厂。</param>
        /// <param name="actionRegistry">动作注册表。</param>
        /// <param name="pipeline">中间件执行管道。</param>
        /// <param name="log">日志输出委托。</param>
        /// <param name="enableUnknownCommandWarning">未知命令走通用回退时是否输出诊断警告日志，默认 true。</param>
        /// <param name="defaultTimeoutMs">步骤未显式配置 TimeoutMs 时的默认超时（毫秒），默认 30000；非正值回退默认。</param>
        /// <exception cref="ArgumentNullException">任意依赖项为 null。</exception>
        public StepDispatcher(
            IStepHandlerLookup handlerLookup,
            IStepHandlerFactory handlerFactory,
            IActionRegistry actionRegistry,
            StepExecutionPipeline pipeline,
            Action<string> log,
            bool enableUnknownCommandWarning = true,
            int defaultTimeoutMs = 30000)
        {
            _handlerLookup = handlerLookup ?? throw new ArgumentNullException(nameof(handlerLookup));
            _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
            _actionRegistry = actionRegistry ?? throw new ArgumentNullException(nameof(actionRegistry));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _log = log ?? (s => { });
            _enableUnknownCommandWarning = enableUnknownCommandWarning;
            _defaultTimeoutMs = defaultTimeoutMs > 0 ? defaultTimeoutMs : 30000;

            // 注册框架内置的通用 Handler 与动作
            ModuleLoader.RegisterBuiltInHandlers(
                this,
                _actionRegistry,
                _handlerFactory,
                _log);

            ValidateNoConflicts();
        }

        /// <summary>
        /// 启动期一致性校验（构造完成后调用）。
        /// 主要检查：注册表内部大小写近似重复、Handler/Action 双注册一致性。
        /// </summary>
        /// <exception cref="InvalidOperationException">检测到仅大小写不同的重复命令时抛出。</exception>
        private void ValidateNoConflicts()
        {
            var registryCommands = GetRegisteredCommands();

            // 检查 handler 注册表内部重复（理论上 Register 已处理，此处作为二次保险）
            var duplicatesInRegistry = registryCommands
                .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatesInRegistry.Any())
            {
                throw new InvalidOperationException($"Handler 注册表存在重复命令: {string.Join(", ", duplicatesInRegistry)}");
            }

            // 检查 action 注册表内部重复
            var actionCommands = _actionRegistry.GetRegisteredActions() ?? Enumerable.Empty<string>();
            var duplicatesInActions = actionCommands
                .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatesInActions.Any())
            {
                throw new InvalidOperationException($"Action 注册表存在重复命令: {string.Join(", ", duplicatesInActions)}");
            }

            // 交叉检查：handler 与 action 是否一致（通过 RegisterHandlerWithAction 应保证一一对应）
            var registrySet = new HashSet<string>(registryCommands, StringComparer.OrdinalIgnoreCase);
            var actionSet = new HashSet<string>(actionCommands, StringComparer.OrdinalIgnoreCase);

            // 交叉提示：仅注册 Handler 未注册 Action 的命令（DynamicFlow DSL 将无法解析其 ActionKey）。
            // 反向（仅注册 Action）属合法常态（纯动作 Provider），不提示，避免启动期误报噪音。
            var onlyInRegistry = registrySet.Except(actionSet, StringComparer.OrdinalIgnoreCase).ToList();

            if (onlyInRegistry.Any())
            {
                _log($"[警告] 以下命令仅注册了 Handler 未注册 Action，DynamicFlow DSL 中无法通过 ActionKey 调用: {string.Join(", ", onlyInRegistry)}");
            }
        }

        /// <summary>
        /// 创建默认的 Handler 查找器（兼容旧代码）。
        /// 使用 <see cref="DefaultStepHandlerProvider"/> 提供模板和回退 Handler。
        /// </summary>
        /// <param name="log">日志输出委托。</param>
        /// <returns>配置完成的 <see cref="IStepHandlerLookup"/> 实例。</returns>
        private static IStepHandlerLookup CreateDefaultLookup(Action<string> log)
        {
            return new RegistryStepHandlerLookup(
                new DefaultStepHandlerFactory(),
                new DefaultStepHandlerProvider());
        }

        /// <summary>
        /// 注册 Handler 到查找策略中。
        /// </summary>
        /// <param name="command">命令名称。</param>
        /// <param name="handler">Handler 实例。</param>
        /// <param name="allowOverwrite">是否允许覆盖已注册的 Handler。</param>
        /// <exception cref="NotSupportedException">当前 HandlerLookup 实现不支持注册。</exception>
        public void RegisterHandler(string command, IStepHandler handler, bool allowOverwrite = true)
        {
            if (_handlerLookup is IRegisterableStepHandlerLookup registrableLookup)
            {
                registrableLookup.Register(command, handler, allowOverwrite);
            }
            else
            {
                throw new NotSupportedException("当前 HandlerLookup 实现不支持注册。");
            }
        }

        /// <summary>
        /// 统一注册 Handler 与 Action，避免调用方遗漏双注册中的任意一侧。
        /// </summary>
        /// <param name="command">命令名称。</param>
        /// <param name="handler">Handler 实例。</param>
        /// <param name="allowOverwrite">是否允许覆盖已注册的 Handler/Action。</param>
        public void RegisterHandlerWithAction(string command, IStepHandler handler, bool allowOverwrite = true)
        {
            RegisterHandler(command, handler, allowOverwrite);
            _actionRegistry.RegisterAction(command, handler.ExecuteAsync,
                allowOverwrite ? RegistrationPolicy.Overwrite : RegistrationPolicy.ThrowIfExists);

            // 缓存 Handler 的 StepHandlerCommandAttribute，供启动期/运行时 EvaluateResult 桥接使用
            var attr = handler.GetType().GetCustomAttribute<StepHandlerCommandAttribute>(true);
            if (attr != null)
            {
                _handlerMetadata[command] = attr;

                // 启动期冲突校验：同名命令参数 schema 不一致
                if (!string.IsNullOrEmpty(attr.ParameterSchema))
                {
                    if (_parameterSchemas.TryGetValue(command, out var existingSchema) && existingSchema != attr.ParameterSchema)
                    {
                        if (allowOverwrite)
                        {
                            _log($"[警告] 命令 '{command}' 参数 schema 被覆盖: '{existingSchema}' -> '{attr.ParameterSchema}'");
                        }
                        else
                        {
                            throw new InvalidOperationException($"命令 '{command}' 存在多个不一致的参数 schema 定义: '{existingSchema}' vs '{attr.ParameterSchema}'");
                        }
                    }
                    _parameterSchemas[command] = attr.ParameterSchema;
                }
            }
        }

        /// <summary>
        /// 按命令前缀移除 Handler 元数据缓存（插件卸载时由 <see cref="ModuleLoader.UnloadPlugin"/> 调用），
        /// 避免热加载/卸载后残留已卸载插件的 attribute 与参数 schema 元数据。
        /// </summary>
        /// <param name="prefix">命令前缀（含尾随点，如 "MyPlugin."）；不区分大小写。</param>
        public void RemoveMetadataByPrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return;

            var keys = _handlerMetadata.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keys)
            {
                _handlerMetadata.TryRemove(key, out _);
                _parameterSchemas.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 获取指定命令对应的 EvaluateResult 元数据（若 Handler 侧通过 <see cref="StepHandlerCommandAttribute"/> 标注）。
        /// </summary>
        /// <param name="command">命令名称。</param>
        /// <returns>若显式设置了 EvaluateResult 则返回其值，否则返回 null（未标注或未显式设置）。</returns>
        public bool? GetEvaluateResult(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            if (_handlerMetadata.TryGetValue(command, out var attr))
            {
                // 仅返回显式设置的值；未显式设置（HasEvaluateResult=false）语义等同 null，避免默认值误触发跳过评估
                return attr.HasEvaluateResult ? attr.EvaluateResult : (bool?)null;
            }
            return null;
        }

        /// <summary>
        /// 获取所有已注册的命令名称（用于启动期冲突扫描）。
        /// </summary>
        /// <returns>已注册命令的只读集合。</returns>
        public IEnumerable<string> GetRegisteredCommands()
        {
            if (_handlerLookup is IRegisterableStepHandlerLookup registrableLookup)
            {
                return registrableLookup.GetRegisteredCommands();
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// 加载模块（程序集/插件目录/扩展实例）。
        /// </summary>
        /// <param name="sources">模块源，支持程序集路径、Assembly 实例、<see cref="IGearExtension"/> 实例。</param>
        public void LoadModules(params object[] sources)
        {
            var loader = new ModuleLoader(this, _actionRegistry, _handlerFactory, _log);
            loader.Load(sources);
            ValidateNoConflicts();
        }

        /// <summary>
        /// 分发并执行单个、原子的测试命令。
        /// 此方法只负责处理一个没有子步骤的 StepConfig，不关心并发、延迟或结果聚合。
        /// </summary>
        /// <param name="step">要执行的步骤配置。</param>
        /// <param name="context">执行上下文，包含设备和父级 CancellationToken。</param>
        /// <returns>一个包含测量结果列表的执行结果。</returns>
        public async Task<ExecutionResult<List<Measurement>>> DispatchSingleAsync(StepConfig step, StepContext context)
        {
            return await _pipeline.ExecuteAsync(step, context, DispatchCoreAsync);
        }

        /// <summary>
        /// 尝试对指定步骤的 Handler 执行健康检查。
        /// 查找顺序与分发一致：注册表 → 模板 → 回退。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>健康检查结果。</returns>
        public Task<HealthCheckResult> TryCheckHealthAsync(StepConfig step, CancellationToken cancellationToken = default)
        {
            return _handlerLookup.TryCheckHealthAsync(step, cancellationToken);
        }

        /// <summary>
        /// 核心分发逻辑：查找 Handler → 设置超时 → 执行 → 规范化结果。
        /// 由 <see cref="StepExecutionPipeline"/> 递归调用。
        /// </summary>
        /// <param name="step">要执行的步骤配置。</param>
        /// <param name="context">执行上下文。</param>
        /// <returns>执行结果。</returns>
        /// <remarks>
        /// 查找顺序：注册表专用 Handler → DSL 模板 Handler → 通用设备调用回退。
        /// 步骤超时由此方法统一施加（Parameters.TimeoutMs / step.TimeoutMs / 默认值），
        /// 并支持 Parameters.TimeoutAction=Fail|Continue；预设管道不再挂 TimeoutMiddleware。
        /// </remarks>
        private async Task<ExecutionResult<List<Measurement>>> DispatchCoreAsync(StepConfig step, StepContext context)
        {
            // 1. 空命令直接返回成功（容器步骤）
            if (string.IsNullOrEmpty(step.Command))
            {
                _log($"{step.StepName} Command 为空，直接返回成功。");
                return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "容器步骤，无命令执行。");
            }

            IStepHandler handler = null;

            // 策略 1: 查找专用 Handler（如 PlcStepHandler）
            if (_handlerLookup.TryGetHandler(step, out var registeredHandler))
            {
                handler = registeredHandler;
                _log($"使用专用 Handler: {step.Command}");
            }
            // 策略 2: 尝试 JSON DSL 模板
            else if (_handlerLookup.TryGetTemplateHandler(step, out var templateHandler))
            {
                handler = templateHandler;
                _log($"使用 DSL 模板执行: {step.Command}");
            }
            // 策略 3: 使用通用设备调用（最终回退）
            else
            {
                if (_enableUnknownCommandWarning)
                {
                    _log($"[警告] 未找到专用 Handler 和 DSL 模板，使用通用设备调用: {step.Command}（如需关闭此警告，请设置 enableUnknownCommandWarning=false）");
                }
                handler = _handlerLookup.GetFallbackHandler(step);
            }

            // 2. 步骤超时（全仓唯一权威：预设管道不再叠加 TimeoutMiddleware）
            // 优先级：Parameters.TimeoutMs > step.TimeoutMs > 构造注入默认值
            int timeoutMs = step.TimeoutMs > 0 ? step.TimeoutMs : _defaultTimeoutMs;
            if (step.Parameters != null
                && step.Parameters.TryGetValue("TimeoutMs", out var timeoutObj)
                && int.TryParse(timeoutObj?.ToString(), out var paramTimeout)
                && paramTimeout > 0)
            {
                timeoutMs = paramTimeout;
            }

            // TimeoutAction：Fail（默认 Failed）/ Continue（仍 Failed 带 [TimeoutContinue]，StopByFail 不掐断；防误 PASS）
            string timeoutAction = "Fail";
            if (step.Parameters != null && step.Parameters.TryGetValue("TimeoutAction", out var timeoutActionObj))
            {
                timeoutAction = timeoutActionObj?.ToString() ?? "Fail";
            }

            using var stepTimeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, stepTimeoutCts.Token);
            var newContext = context.WithToken(linkedCts.Token);

            // 3. 执行并规范化返回类型
            try
            {
                var rawResult = await handler.ExecuteAsync(step, newContext).ConfigureAwait(false);
                _log($"[DispatchCoreAsync] step={step.StepName}, command={step.Command}, handler={handler.GetType().FullName}, success={rawResult.Success}, message={rawResult.Message}");

                // 已经是列表结果，直接返回
                if (rawResult is ExecutionResult<List<Measurement>> listResult) return listResult;

                if (rawResult.Success)
                {
                    // 单测量值包装为列表
                    if (rawResult.GetValueAsObject() is Measurement single)
                        return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement> { single });

                    return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), rawResult.SamplesCollected, rawResult.Message);
                }

                // 失败场景：单测量值包装为列表
                if (rawResult.GetValueAsObject() is Measurement failSingle)
                    return ExecutionResult<List<Measurement>>.Failed(rawResult.Message, new List<Measurement> { failSingle });

                return ExecutionResult<List<Measurement>>.Failed(rawResult.Message);
            }
            catch (OperationCanceledException) when (stepTimeoutCts.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
            {
                // 本步骤超时（非上层取消）
                _log($"[Timeout] 步骤 '{step.StepName}' 执行超时 ({timeoutMs}ms), TimeoutAction={timeoutAction}");
                if (string.Equals(timeoutAction, "Continue", StringComparison.OrdinalIgnoreCase))
                {
                    // Continue：不中断序列的意图由执行器结合 StopByFail 理解；
                    // 此处必须返回 Failed，避免 EvaluateResult=false / Execute 把空测量当成 PASS（防漏检）。
                    return ExecutionResult<List<Measurement>>.Failed(
                        $"[TimeoutContinue] 步骤执行超时 ({timeoutMs}ms)",
                        new List<Measurement>());
                }

                return ExecutionResult<List<Measurement>>.Failed(
                    $"步骤执行超时 ({timeoutMs}ms)",
                    new List<Measurement>());
            }
            catch (OperationCanceledException)
            {
                return ExecutionResult<List<Measurement>>.Failed("操作超时或被取消。");
            }
            catch (Exception ex)
            {
                _log($"[StepDispatcher] Exception in step {step.StepName}: {ex}");
                return ExecutionResult<List<Measurement>>.Failed($"执行错误: {ex.ToString().Replace("\r", "").Replace("\n", " ")}");
            }
        }
    }
}
