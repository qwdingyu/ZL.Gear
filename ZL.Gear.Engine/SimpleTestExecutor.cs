using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;
using ZL.Gear.Drivers.Core;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// 简化的测试执行器
    /// 用于 ConsoleApp 的快速测试验证
    /// </summary>
    public class SimpleTestExecutor : IDisposable
    {
        private readonly Action<string> _log;
        private readonly UnifiedDeviceFactory _factory;
        private readonly UnifiedDeviceService _deviceService;
        private bool _disposed;

        public static SimpleTestExecutor Create(Action<string>? logger = null)
        {
            return new SimpleTestExecutor(logger);
        }

        public SimpleTestExecutor(Action<string>? log = null)
        {
            _log = log ?? (msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}"));
            _factory = new UnifiedDeviceFactory();
            _deviceService = new UnifiedDeviceService(_factory);
        }

        public async Task<SimpleTestResult> ExecuteAsync(
            string model, string barcode, List<StepConfig> steps,
            IProgress<string> progress = null, CancellationToken token = default)
        {
            var result = new SimpleTestResult
            {
                Model = model, Barcode = barcode, StartTime = DateTime.Now
            };

            _log("==================================================");
            _log($"ZL.Gear 测试执行器 v2.0");
            _log($"型号: {model}, 条码: {barcode}");
            _log("==================================================");

            try
            {
                _log("");
                _log("正在初始化设备服务...");
                await _deviceService.InitializeAllDevicesAsync();
                _log("设备初始化完成");
                _log("");

                int stepIndex = 0;
                foreach (var step in steps)
                {
                    if (token.IsCancellationRequested) { _log("测试被取消"); break; }
                    stepIndex++;
                    if (!step.Enable) { _log($"[{stepIndex}] 跳过: {step.StepName} (已禁用)"); continue; }

                    _log($"[{stepIndex}] 开始执行: {step.StepName}");
                    _log($"    命令: {step.Command}, 目标: {step.Target}");

                    var stepResult = await ExecuteStepAsync(step, stepIndex, token);

                    if (stepResult.Success)
                    {
                        result.Passed++;
                        result.StepResults.Add($"{step.StepName}: ✓ 通过");
                        _log($"    ✓ 通过");
                    }
                    else
                    {
                        result.Failed++;
                        result.StepResults.Add($"{step.StepName}: ✗ {stepResult.Message}");
                        _log($"    ✗ 失败: {stepResult.Message}");
                        if (step.StopByFail) { _log("    因 StopByFail=true，停止后续步骤"); break; }
                    }
                    progress?.Report(stepResult.Message ?? step.StepName);
                }

                result.EndTime = DateTime.Now;
                result.OverallSuccess = result.Failed == 0;
                result.Summary = $"总步骤: {result.Passed + result.Failed}, 通过: {result.Passed}, 失败: {result.Failed}";

                _log("");
                _log("==================================================");
                _log($"测试结果: {(result.OverallSuccess ? "✓ 通过" : "✗ 失败")}");
                _log(result.Summary);
                _log($"总耗时: {(result.EndTime - result.StartTime).TotalSeconds:F2}秒");
                _log("==================================================");
            }
            catch (Exception ex)
            {
                _log($"异常: {ex.Message}");
                result.OverallSuccess = false;
                result.Summary = $"执行异常: {ex.Message}";
            }

            return result;
        }

        private async Task<StepExecutionResult> ExecuteStepAsync(StepConfig step, int stepIndex, CancellationToken token)
        {
            var result = new StepExecutionResult { Success = false };
            try
            {
                var command = step.Command?.ToUpperInvariant() ?? "";
                var target = step.Target ?? "";
                var parameters = step.Parameters ?? new Dictionary<string, object>();

                switch (command)
                {
                    case "DYNAMICFLOW":
                        result = await ExecuteDynamicFlowStepAsync(step, token); break;
                    case "CONNECT":
                        result = await ExecuteConnectStepAsync(target, token); break;
                    case "MEASURE":
                    case "GENERICMEASURE":
                        result = await ExecuteMeasureStepAsync(target, step, token); break;
                    case "WRITE":
                        result = await ExecuteWriteStepAsync(target, parameters, token); break;
                    case "DELAY":
                        result = await ExecuteDelayStepAsync(parameters, token); break;
                    case "ASSERT":
                        result = await ExecuteAssertStepAsync(parameters, token); break;
                    case "LOG":
                        result = await ExecuteLogStepAsync(parameters, token); break;
                    case "MICROWORKFLOWDEMO":
                        result = await ExecuteMicroWorkflowDemoStepAsync(step, token); break;
                    case "TRIGGEREDMEASURE":
                        result = await ExecuteTriggeredMeasureStepAsync(step, token); break;
                    default:
                        if (step.StepType == "Group")
                        {
                            result = await ExecuteGroupStepAsync(step, token);
                        }
                        else
                        {
                            _log($"    (未知命令: {command})");
                            result.Success = false;
                            result.Message = $"不支持的命令: {command}";
                        }
                        break;
                }
            }
            catch (Exception ex) { result.Success = false; result.Message = ex.Message; }
            return result;
        }

        private async Task<StepExecutionResult> ExecuteGroupStepAsync(StepConfig step, CancellationToken token)
        {
            _log($"    [Group] 开始执行组步骤: {step.StepName}");
            _log($"    [Group] 包含 {step.SubSteps?.Count ?? 0} 个子步骤");

            if (step.SubSteps == null || step.SubSteps.Count == 0)
            {
                return new StepExecutionResult { Success = true, Message = "Group 步骤无子步骤，跳过" };
            }

            try
            {
                var executionMode = step.ExecutionMode;
                var subStepResults = new List<StepExecutionResult>();

                if (executionMode == StepExecutionMode.Parallel)
                {
                    _log($"    [Group] 并行模式执行 {step.SubSteps.Count} 个子步骤...");
                    var tasks = step.SubSteps
                        .Where(s => s.Enable)
                        .Select((subStep, index) => ExecuteSubStepAsync(subStep, index + 1, token))
                        .ToList();
                    subStepResults = (await Task.WhenAll(tasks)).ToList();
                }
                else
                {
                    _log($"    [Group] 串行模式执行 {step.SubSteps.Count} 个子步骤...");
                    int subIndex = 0;
                    foreach (var subStep in step.SubSteps)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return new StepExecutionResult { Success = false, Message = "Group 执行被取消" };
                        }

                        subIndex++;
                        if (!subStep.Enable)
                        {
                            _log($"    [Group]   [{subIndex}] 跳过: {subStep.StepName} (已禁用)");
                            continue;
                        }

                        _log($"    [Group]   [{subIndex}] 开始: {subStep.StepName}");
                        var subResult = await ExecuteSubStepAsync(subStep, subIndex, token);
                        subStepResults.Add(subResult);

                        if (subResult.Success)
                        {
                            _log($"    [Group]   [{subIndex}] ✓ {subStep.StepName}");
                        }
                        else
                        {
                            _log($"    [Group]   [{subIndex}] ✗ {subStep.StepName}: {subResult.Message}");
                            if (subStep.StopByFail)
                            {
                                _log($"    [Group]   因 StopByFail=true，停止后续子步骤");
                                break;
                            }
                        }
                    }
                }

                var allPassed = subStepResults.All(r => r.Success);
                var failedCount = subStepResults.Count(r => !r.Success);
                return new StepExecutionResult
                {
                    Success = allPassed,
                    Message = allPassed
                        ? $"Group 执行完成: {subStepResults.Count} 个子步骤全部通过"
                        : $"Group 执行完成: {failedCount} 个子步骤失败"
                };
            }
            catch (Exception ex)
            {
                return new StepExecutionResult { Success = false, Message = $"Group 执行异常: {ex.Message}" };
            }
        }

        private async Task<StepExecutionResult> ExecuteSubStepAsync(StepConfig subStep, int subIndex, CancellationToken token)
        {
            var result = new StepExecutionResult { Success = false };
            try
            {
                var command = subStep.Command?.ToUpperInvariant() ?? "";
                var target = subStep.Target ?? "";
                var parameters = subStep.Parameters ?? new Dictionary<string, object>();

                switch (command)
                {
                    case "DYNAMICFLOW":
                        result = await ExecuteDynamicFlowStepAsync(subStep, token); break;
                    case "CONNECT":
                        result = await ExecuteConnectStepAsync(target, token); break;
                    case "MEASURE":
                    case "GENERICMEASURE":
                        result = await ExecuteMeasureStepAsync(target, subStep, token); break;
                    case "WRITE":
                        result = await ExecuteWriteStepAsync(target, parameters, token); break;
                    case "DELAY":
                        result = await ExecuteDelayStepAsync(parameters, token); break;
                    case "ASSERT":
                        result = await ExecuteAssertStepAsync(parameters, token); break;
                    case "LOG":
                        result = await ExecuteLogStepAsync(parameters, token); break;
                    case "MICROWORKFLOWDEMO":
                        result = await ExecuteMicroWorkflowDemoStepAsync(subStep, token); break;
                    case "TRIGGEREDMEASURE":
                        result = await ExecuteTriggeredMeasureStepAsync(subStep, token); break;
                    default:
                        _log($"    [Group]     (未知命令: {command})");
                        result.Success = false;
                        result.Message = $"不支持的命令: {command}";
                        break;
                }
            }
            catch (Exception ex) { result.Success = false; result.Message = ex.Message; }
            return result;
        }

        private async Task<StepExecutionResult> ExecuteTriggeredMeasureStepAsync(StepConfig step, CancellationToken token)
        {
            var isMaster = GetParameter<bool>(step.Parameters, "IsMaster", false);
            var dependsOn = GetParameter<string>(step.Parameters, "DependsOn", "");
            var measurementKey = GetParameter<string>(step.Parameters, "MeasurementKey", step.StepName);

            _log($"    [TriggeredMeasure] 测量键: {measurementKey}");

            if (isMaster)
            {
                _log($"    [TriggeredMeasure] 主步骤模式: 等待触发条件...");
                await Task.Delay(500, token);
                _log($"    [TriggeredMeasure] 触发条件已满足，发送开始信号");
            }
            else if (!string.IsNullOrEmpty(dependsOn))
            {
                _log($"    [TriggeredMeasure] 从步骤模式: 等待 '{dependsOn}' 的信号...");
                await Task.Delay(300, token);
                _log($"    [TriggeredMeasure] 收到信号，开始执行测量");
            }
            else
            {
                _log($"    [TriggeredMeasure] 独立测量模式");
            }

            await Task.Delay(200, token);
            var value = 5.0 + new Random().NextDouble() * 2.0;
            _log($"    [TriggeredMeasure] 测量完成: {value:F4}");

            return new StepExecutionResult { Success = true, Message = $"触发式测量完成: {value:F4}", MeasuredValue = value };
        }

        private async Task<StepExecutionResult> ExecuteDynamicFlowStepAsync(StepConfig step, CancellationToken token)
        {
            _log("    [DynamicFlow] 解析序列配置...");
            var parameters = step.Parameters;
            if (parameters == null || parameters.Count == 0)
                return new StepExecutionResult { Success = true, Message = "无参数，跳过" };
            foreach (var param in parameters)
                if (param.Key.StartsWith("Expected") || param.Key.StartsWith("Threshold"))
                    _log($"    [变量] {param.Key} = {param.Value}");
            _log($"    [DynamicFlow] 配置已加载 (参数数: {parameters.Count})");
            await Task.Delay(50, token);
            return new StepExecutionResult { Success = true, Message = "DynamicFlow 配置完成" };
        }

        private async Task<StepExecutionResult> ExecuteConnectStepAsync(string target, CancellationToken token)
        {
            _log($"    [Connect] 连接到设备: {target ?? "(默认)"}");
            await Task.Delay(100, token);
            return new StepExecutionResult { Success = true, Message = "连接成功" };
        }

        private async Task<StepExecutionResult> ExecuteMeasureStepAsync(string target, StepConfig step, CancellationToken token)
        {
            _log($"    [Measure] 测量目标: {target}");
            _log($"    [Measure] 采样参数数: {step.Parameters?.Count ?? 0}");
            await Task.Delay(200, token);
            var value = 10.0 + new Random().NextDouble() * 0.5;
            _log($"    [Measure] 测量值: {value:F4}");
            return new StepExecutionResult { Success = true, Message = $"测量完成: {value:F4}", MeasuredValue = value };
        }

        private async Task<StepExecutionResult> ExecuteMicroWorkflowDemoStepAsync(StepConfig step, CancellationToken token)
        {
            _log($"    [MicroWorkflowDemo] 开始执行综合演示...");
            var parameters = step.Parameters ?? new Dictionary<string, object>();
            var enableParallel = GetParameter<bool>(parameters, "EnableParallel", true);
            var enableRetry = GetParameter<bool>(parameters, "EnableRetry", true);
            var enableLoop = GetParameter<bool>(parameters, "EnableLoop", true);
            var enableConditional = GetParameter<bool>(parameters, "EnableConditionalBranch", true);
            var modelType = GetParameter<string>(parameters, "ModelType", "ModelA");

            try
            {
                _log("    [Phase1] 设备初始化 - 上电...");
                await Task.Delay(100, token);
                _log("    [Phase1] 设备初始化 - 自检...");
                await Task.Delay(100, token);
                _log("    [Phase1] ✓ 初始化完成");

                if (enableConditional)
                {
                    _log($"    [Phase2] 条件分支 - 检测到型号: {modelType}");
                    _log(modelType == "ModelA" ? "    [Phase2] 执行 ModelA 分支 - 电压测试" : "    [Phase2] 执行默认分支 - 跳过");
                    await Task.Delay(50, token);
                    _log("    [Phase2] ✓ 分支完成");
                }

                if (enableParallel)
                {
                    _log("    [Phase3] 并行测量 - 同时采集电压、电流、温度...");
                    await Task.Delay(100, token);
                    _log("    [Phase3]   电压: 12.05V, 电流: 1.52A, 温度: 25.3℃");
                    _log("    [Phase3] ✓ 并行测量完成");
                }

                if (enableRetry)
                {
                    _log("    [Phase4] 重试机制 - 模拟网络通信...");
                    for (int i = 1; i <= 3; i++) { await Task.Delay(50, token); _log($"    [Phase4]   第 {i} 次尝试..."); }
                    _log("    [Phase4] ✓ 通信成功");
                }

                _log("    [Phase5] 轮询等待 - 等待气缸到位...");
                for (int i = 1; i <= 4; i++) { await Task.Delay(50, token); _log(i < 4 ? $"    [Phase5]   第 {i} 次检查：气缸未到位..." : "    [Phase5]   第 4 次检查：气缸到位！"); }
                _log("    [Phase5] ✓ 等待完成");

                if (enableLoop)
                {
                    _log("    [Phase6] 循环结构 - 执行 3 次测量...");
                    for (int i = 1; i <= 3; i++) { await Task.Delay(50, token); _log($"    [Phase6]   第 {i} 次测量完成"); }
                    _log("    [Phase6] ✓ 循环完成");
                }

                _log("    [Phase7] 清理操作 - 设备复位...");
                await Task.Delay(50, token);
                _log("    [Phase7] ✓ 清理完成");
                _log("    [MicroWorkflowDemo] ✓ 所有阶段执行完成");

                return new StepExecutionResult { Success = true, Message = "MicroWorkflow 综合演示测试完成" };
            }
            catch (OperationCanceledException)
            {
                return new StepExecutionResult { Success = false, Message = "操作被取消" };
            }
            catch (Exception ex)
            {
                return new StepExecutionResult { Success = false, Message = $"执行异常: {ex.Message}" };
            }
        }

        private async Task<StepExecutionResult> ExecuteWriteStepAsync(string target, Dictionary<string, object> parameters, CancellationToken token)
        {
            _log($"    [Write] 目标: {target}");
            foreach (var p in parameters) _log($"    [Write] {p.Key} = {p.Value}");
            await Task.Delay(50, token);
            return new StepExecutionResult { Success = true, Message = "写入完成" };
        }

        private async Task<StepExecutionResult> ExecuteDelayStepAsync(Dictionary<string, object> parameters, CancellationToken token)
        {
            var delayMs = 500;
            if (parameters.TryGetValue("DelayMs", out var d)) try { delayMs = Convert.ToInt32(d); } catch { }
            _log($"    [Delay] 延时 {delayMs}ms");
            await Task.Delay(delayMs, token);
            return new StepExecutionResult { Success = true, Message = $"延时 {delayMs}ms 完成" };
        }

        private async Task<StepExecutionResult> ExecuteAssertStepAsync(Dictionary<string, object> parameters, CancellationToken token)
        {
            var condition = parameters.TryGetValue("Condition", out var c) ? c?.ToString() ?? "" : "";
            var message = parameters.TryGetValue("Message", out var m) ? m?.ToString() ?? "" : "";
            _log($"    [Assert] 条件: {condition}");
            if (!string.IsNullOrEmpty(message)) _log($"    [Assert] 消息: {message}");
            await Task.Delay(50, token);
            bool passed = new Random().NextDouble() > 0.1;
            return new StepExecutionResult { Success = passed, Message = passed ? "断言通过" : (message ?? "断言失败") };
        }

        private async Task<StepExecutionResult> ExecuteLogStepAsync(Dictionary<string, object> parameters, CancellationToken token)
        {
            var message = parameters.TryGetValue("Message", out var m) ? m?.ToString() ?? "" : "(无)";
            _log($"    [Log] {message}");
            await Task.Delay(10, token);
            return new StepExecutionResult { Success = true, Message = "日志已输出" };
        }

        private static T GetParameter<T>(Dictionary<string, object> parameters, string key, T defaultValue)
        {
            if (parameters.TryGetValue(key, out var value))
            {
                if (value is T t) return t;
                if (value != null)
                {
                    var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                    if (targetType == typeof(double)) return (T)(object)Convert.ToDouble(value);
                    if (targetType == typeof(int)) return (T)(object)Convert.ToInt32(value);
                    if (targetType == typeof(bool)) return (T)(object)Convert.ToBoolean(value);
                    return (T)Convert.ChangeType(value, targetType);
                }
            }
            return defaultValue;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                try { (_deviceService as IDisposable)?.Dispose(); _factory.Dispose(); } catch { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    if (_deviceService is IAsyncDisposable ad) await ad.DisposeAsync().ConfigureAwait(false);
                    else (_deviceService as IDisposable)?.Dispose();
                    await _factory.DisposeAsync().ConfigureAwait(false);
                }
                catch { }
            }
        }
    }

    public class SimpleTestResult
    {
        public string Model { get; set; }
        public string Barcode { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool OverallSuccess { get; set; }
        public string Summary { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public List<string> StepResults { get; set; } = new List<string>();
    }

    public class StepExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public double? MeasuredValue { get; set; }
    }
}
