using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZL.Gear.Core;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Services;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Drivers.Devices;
using ZL.Gear.Engine.Workflow;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine.Runner
{
    public interface ITestExecutionService
    {
        void EnqueueTest(TestRequest request);
        void CancelAll();
        Task EmergencyStopAsync(CancellationToken token = default);
    }

    public sealed class CentralizedTestExecutionService : ITestExecutionService, IDisposable
    {
        private static CentralizedTestExecutionService _instance;
        private static readonly object _lock = new object();
        private static bool _isInitialized = false;

        private readonly UnifiedDeviceService _deviceService;
        private readonly SequenceExecutor _executor;
        private readonly Action<string> _log;

        private readonly BlockingCollection<TestRequest> _requestQueue = new BlockingCollection<TestRequest>();
        private readonly CancellationTokenSource _serviceShutdownCts = new CancellationTokenSource();
        private readonly Task _consumerTask;
        private readonly int TestStepInterval = 500;

        public static CentralizedTestExecutionService Instance
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException("CentralizedTestExecutionService has not been initialized. Please call Initialize() first.");
                }
                return _instance;
            }
        }

        public static void Initialize(Action<string> log, int testStepInterval)
        {
            if (_isInitialized) { return; }

            lock (_lock)
            {
                if (_isInitialized) { return; }

                if (log == null) throw new ArgumentNullException(nameof(log));
                _instance = new CentralizedTestExecutionService(log, testStepInterval);
                _isInitialized = true;
            }
        }

        private CentralizedTestExecutionService(Action<string> log, int testStepInterval = 500)
        {
            _log = log;
            TestStepInterval = testStepInterval;

            var factory = new UnifiedDeviceFactory();
            _deviceService = new UnifiedDeviceService(factory);

            var actionService = new WorkflowActionService(log);
            var logger = new SequenceExecutorLogger(log);

            _executor = new SequenceExecutor(
                _deviceService,
                GearProfileServices.Instance,
                logger,
                TestStepInterval);

            log("[服务启动] 正在初始化所有设备...");
            try
            {
                _deviceService.InitializeAllDevicesAsync(1, token: _serviceShutdownCts.Token).GetAwaiter().GetResult();
                log("[服务启动] 所有设备初始化成功。");
            }
            catch (Exception ex)
            {
                log($"[服务启动][严重错误] 设备初始化失败，服务可能无法正常工作: {ex}");
                throw;
            }

            _consumerTask = Task.Run(() => ProcessQueueAsync(_serviceShutdownCts.Token));
            log("[服务启动] 测试执行服务已启动并正在监听请求。");
        }

        private async Task ProcessQueueAsync(CancellationToken shutdownToken)
        {
            try
            {
                foreach (var request in _requestQueue.GetConsumingEnumerable(shutdownToken))
                {
                    _log($"[执行服务] 从队列中获取新测试请求 (Barcode: {request.Barcode})，开始执行...");
                    TestRunResult result = null;

                    try
                    {
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, request.CancellationToken);
                        var progress = request.StepProgressReporter != null 
                            ? new ActionProgress<StepRunResult>(request.StepProgressReporter) 
                            : null;
                        result = await _executor.ExecuteAsync(
                            request.Steps,
                            string.Empty,
                            request.Barcode,
                            null,
                            linkedCts.Token,
                            progress);
                    }
                    catch (OperationCanceledException)
                    {
                        _log($"[执行服务] 测试 (Barcode: {request.Barcode}) 被取消。");
                    }
                    catch (Exception ex)
                    {
                        _log($"[执行服务][严重错误] 执行测试 (Barcode: {request.Barcode}) 时发生未捕获的异常: {ex}");
                    }

                    if (result != null && request.ResultReporter != null)
                    {
                        try { request.ResultReporter.Report(result); }
                        catch (Exception ex) { _log($"[执行服务][警告] 向调用方报告结果时发生异常: {ex.Message}"); }
                    }
                    _log($"[执行服务] 测试 (Barcode: {request.Barcode}) 执行完成。");
                }
            }
            catch (OperationCanceledException)
            {
                _log("[执行服务] 消费者任务已停止。");
            }
            catch (Exception ex)
            {
                _log($"[执行服务][致命错误] 队列处理器崩溃: {ex}。服务已停止。");
            }
        }

        public void EnqueueTest(TestRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (_serviceShutdownCts.IsCancellationRequested || _requestQueue.IsAddingCompleted)
            {
                _log("[执行服务][警告] 服务已关闭，无法添加新的测试请求。");
                return;
            }
            _requestQueue.Add(request);
            _log($"[执行服务] 新测试请求 (Barcode: {request.Barcode}) 已入队。当前队列深度: {_requestQueue.Count}");
        }

        public void CancelAll()
        {
            _log("[执行服务] 收到全部取消指令。");
            _serviceShutdownCts.Cancel();
            while (_requestQueue.TryTake(out _)) { }
            _requestQueue.CompleteAdding();
        }

        public async Task EmergencyStopAsync(CancellationToken token = default)
        {
            await _deviceService.EmergencyStopAsync(token);
        }

        public void Dispose()
        {
            _log("[服务关闭] 开始关闭测试执行服务...");
            if (!_serviceShutdownCts.IsCancellationRequested)
            {
                CancelAll();
            }

            _consumerTask.Wait(TimeSpan.FromSeconds(5));

            _serviceShutdownCts.Dispose();
            _requestQueue.Dispose();

            _executor.Dispose();
            _log("[服务关闭] 测试执行服务已成功关闭。");
        }

        private class ActionProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;
            public ActionProgress(Action<T> callback) => _callback = callback;
            public void Report(T value) => _callback?.Invoke(value);
        }

        private class SequenceExecutorLogger : ILogger<SequenceExecutor>
        {
            private readonly Action<string> _log;
            public SequenceExecutorLogger(Action<string> log) => _log = log;
            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                _log?.Invoke(formatter(state, exception));
            }
        }
    }
}
