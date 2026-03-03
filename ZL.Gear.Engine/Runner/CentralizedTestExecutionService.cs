using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZL.Gear.Core;
using ZL.Gear.Core.Configuration;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Services;
using ZL.Gear.Drivers.Core;

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

        private readonly IDeviceService _deviceService;
        private readonly SequenceExecutor _executor;
        private readonly IGearProfileService _profileService;
        private readonly Action<string> _log;

        private readonly BlockingCollection<TestRequest> _requestQueue = new BlockingCollection<TestRequest>();
        private readonly CancellationTokenSource _serviceShutdownCts = new CancellationTokenSource();
        private readonly Task _consumerTask;
        private readonly int _testStepInterval;

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

        public static void Initialize(ILibraryService libraryService, Action<string> log, int testStepInterval = 500)
        {
            if (_isInitialized) { return; }

            lock (_lock)
            {
                if (_isInitialized) { return; }

                if (log == null) throw new ArgumentNullException(nameof(log));
                if (libraryService == null) throw new ArgumentNullException(nameof(libraryService));

                _instance = new CentralizedTestExecutionService(libraryService, log, testStepInterval);
                _isInitialized = true;
            }
        }

        private CentralizedTestExecutionService(ILibraryService libraryService, Action<string> log, int testStepInterval)
        {
            _log = log;
            _testStepInterval = testStepInterval;

            // 初始化核心服务
            var factory = new UnifiedDeviceFactory();
            _deviceService = new UnifiedDeviceService(factory);
            _profileService = new GearProfileServices(libraryService);
            
            var logger = new SequenceExecutorLogger(log);

            // 构建执行器
            _executor = new SequenceExecutor(
                _deviceService,
                _profileService,
                logger,
                _testStepInterval);

            _log("[服务启动] 正在初始化所有设备...");
            try
            {
                // 注意：在构造函数中执行异步等待可能存在风险，但在 Singleton 初始化阶段通常是可接受的
                _deviceService.InitializeAllDevicesAsync(1, token: _serviceShutdownCts.Token).GetAwaiter().GetResult();
                _log("[服务启动] 所有设备初始化成功。");
            }
            catch (Exception ex)
            {
                _log($"[服务启动][严重错误] 设备初始化失败: {ex}");
                throw;
            }

            _consumerTask = Task.Run(() => ProcessQueueAsync(_serviceShutdownCts.Token));
            _log("[服务启动] 测试执行服务已就绪。");
        }

        private async Task ProcessQueueAsync(CancellationToken shutdownToken)
        {
            try
            {
                foreach (var request in _requestQueue.GetConsumingEnumerable(shutdownToken))
                {
                    _log($"[执行服务] 开始执行请求 (Barcode: {request.Barcode})...");
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
                        _log($"[执行服务] 请求 (Barcode: {request.Barcode}) 已取消。");
                    }
                    catch (Exception ex)
                    {
                        _log($"[执行服务][错误] 执行异常 (Barcode: {request.Barcode}): {ex}");
                    }

                    if (result != null && request.ResultReporter != null)
                    {
                        try { request.ResultReporter.Report(result); }
                        catch (Exception ex) { _log($"[执行服务][警告] 报告结果失败: {ex.Message}"); }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log($"[执行服务][致命错误] 队列处理器崩溃: {ex}");
            }
        }

        public void EnqueueTest(TestRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_serviceShutdownCts.IsCancellationRequested) return;
            
            _requestQueue.Add(request);
        }

        public void CancelAll()
        {
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
            _log("[服务关闭] 正在停止服务...");
            CancelAll();
            _consumerTask.Wait(TimeSpan.FromSeconds(5));
            _serviceShutdownCts.Dispose();
            _requestQueue.Dispose();
            _executor.Dispose();
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
