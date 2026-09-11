using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ZL.Gear.Communication.Guard
{
    /// <summary>
    /// 连接卫士：负责连接生命周期、自动重连、心跳保活与发送串行化。
    /// 设计目标：确定性停机、线程安全、异常隔离、可观测性。
    /// </summary>
    public sealed class ConnectionGuard : IDisposable
    {
        private readonly IChannelAdapter _channel;
        private readonly ConnectionGuardOptions _options;
        private readonly IGuardLogger _logger;
        private readonly CancellationTokenSource _lifecycleCts = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> _maintenanceLoopStopped =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly Channel<(GuardState state, string message)> _stateChannel =
            Channel.CreateUnbounded<(GuardState, string)>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        private Task _stateLoopTask;
        private volatile int _connectionState = (int)GuardState.Disconnected;
        private int _isRunning;
        private DateTime _lastReceiveTime = DateTime.MinValue;
        private DateTime _lastSendTime = DateTime.MinValue;
        private DateTime _lastHeartbeatErrorLogTime = DateTime.MinValue;
        private DateTime _connectedAt = DateTime.MinValue;
        private bool _hasSentSinceConnect;

        public ConnectionGuard(IChannelAdapter channel, ConnectionGuardOptions options = null, IGuardLogger logger = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _options = options ?? new ConnectionGuardOptions();
            _logger = logger ?? NullLogger.Instance;
            _channel.OnDataReceived += HandleRawData;
        }

        public GuardState CurrentState => (GuardState)_connectionState;

        public event Action<GuardState, string> OnStateChanged;
        public event Action<byte[]> OnDataReceived;

        public void Start()
        {
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            {
                _logger.Warn("Guard is already running.");
                return;
            }

            _stateLoopTask = Task.Run(StateEventLoop, _lifecycleCts.Token);
            Task.Factory.StartNew(MaintenanceLoop, _lifecycleCts.Token,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default)
                .Unwrap()
                .ContinueWith(t =>
                {
                    _maintenanceLoopStopped.TrySetResult(true);
                    if (t.IsFaulted)
                    {
                        _logger.Error(t.Exception, "CRITICAL: MaintenanceLoop crashed!");
                        SetState(GuardState.Faulted, $"Internal Error: {t.Exception?.InnerException?.Message}");
                    }
                }, TaskScheduler.Default);
        }

        public Task StopAsync()
        {
            if (_lifecycleCts.IsCancellationRequested) return Task.CompletedTask;
            _logger.Info("Stopping guard...");
            _lifecycleCts.Cancel();
            return _maintenanceLoopStopped.Task;
        }

        public void Stop()
        {
            StopAsync().Wait(TimeSpan.FromSeconds(5));
        }

        public async Task<bool> SendAsync(byte[] data)
        {
            return await SendAsync(data, SendPriority.Normal);
        }

        public async Task<bool> SendAsync(byte[] data, SendPriority priority)
        {
            if (CurrentState != GuardState.Connected) return false;
            if (data == null || data.Length == 0) return false;
            return await TrySendInternalAsync(data);
        }

        private async Task MaintenanceLoop()
        {
            _logger.Info("Maintenance loop started.");
            int currentDelay = _options.ReconnectMinDelayMs;

            while (!_lifecycleCts.IsCancellationRequested)
            {
                try
                {
                    if (!_channel.IsConnected)
                    {
                        if (CurrentState == GuardState.Connected)
                        {
                            SetState(GuardState.Reconnecting, "Connection lost.");
                        }
                        else if (CurrentState == GuardState.Disconnected)
                        {
                            SetState(GuardState.Connecting, "Connecting...");
                        }

                        try
                        {
                            await _channel.OpenAsync(_lifecycleCts.Token);
                            SetState(GuardState.Connected, "Connected.");
                            currentDelay = _options.ReconnectMinDelayMs;
                            _connectedAt = DateTime.Now;
                            _lastReceiveTime = DateTime.Now;
                            _lastSendTime = DateTime.Now;
                            _hasSentSinceConnect = false;
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn($"Connection failed: {ex.Message}. Retry in {currentDelay}ms");
                            double jitterFactor = Math.Max(0, _options.ReconnectJitterFactor);
                            int jitter = jitterFactor > 0
                                ? (int)Math.Round(currentDelay * (new Random().NextDouble() * jitterFactor))
                                : 0;
                            await Task.Delay(currentDelay + jitter, _lifecycleCts.Token);
                            currentDelay = Math.Min(currentDelay * 2, _options.ReconnectMaxDelayMs);
                            continue;
                        }
                    }

                    if (_channel.IsConnected)
                    {
                        var now = DateTime.Now;

                        if (_options.DeviceDeadTimeoutMs > 0
                            && ShouldCheckWatchdog(now)
                            && (now - _lastReceiveTime).TotalMilliseconds > _options.DeviceDeadTimeoutMs)
                        {
                            _logger.Warn($"Watchdog timeout ({(now - _lastReceiveTime).TotalSeconds:F1}s). Reconnecting...");
                            TriggerReconnect();
                            continue;
                        }

                        if (ShouldSendHeartbeat(now))
                        {
                            await ExecuteHeartbeatAsync();
                        }
                    }

                    await Task.Delay(_options.MaintenanceLoopDelayMs, _lifecycleCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unexpected error in MaintenanceLoop.");
                    await Task.Delay(1000, _lifecycleCts.Token);
                }
            }

            SetState(GuardState.Disconnected, "Maintenance loop stopped.");
            await SafeCloseChannelAsync();
        }

        private bool ShouldSendHeartbeat(DateTime now)
        {
            if (_options.HeartbeatStrategy == null) return false;
            return (now - _lastSendTime).TotalMilliseconds > _options.HeartbeatIntervalMs;
        }

        private async Task ExecuteHeartbeatAsync()
        {
            try
            {
                byte[] payload = null;
                if (_options.HeartbeatStrategy != null)
                {
                    payload = _options.HeartbeatStrategy.CreateHeartbeat();
                }

                if (payload != null && payload.Length > 0)
                {
                    _logger.Debug("Sending heartbeat...");
                    await SendAsync(payload, SendPriority.High);
                }
            }
            catch (Exception ex)
            {
                if ((DateTime.Now - _lastHeartbeatErrorLogTime).TotalSeconds > 10)
                {
                    _logger.Warn($"Heartbeat generation failed: {ex.Message}");
                    _lastHeartbeatErrorLogTime = DateTime.Now;
                }
            }
        }

        private void HandleRawData(byte[] data)
        {
            _lastReceiveTime = DateTime.Now;
            try
            {
                OnDataReceived?.Invoke(data);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in OnDataReceived user callback.");
            }
        }

        private void TriggerReconnect()
        {
            Task.Run(SafeCloseChannelAsync);
        }

        private async Task SafeCloseChannelAsync()
        {
            try
            {
                await _channel.CloseAsync();
            }
            catch { }
        }

        private void SetState(GuardState state, string message)
        {
            int oldState = Interlocked.Exchange(ref _connectionState, (int)state);
            if (oldState == (int)state) return;
            _logger.Info(message);
            _stateChannel.Writer.TryWrite((state, message));
        }

        private async Task StateEventLoop()
        {
            try
            {
                while (!_lifecycleCts.IsCancellationRequested)
                {
                    var readTask = _stateChannel.Reader.ReadAsync(_lifecycleCts.Token).AsTask();
                    var delayTask = Task.Delay(-1, _lifecycleCts.Token);
                    var completedTask = await Task.WhenAny(readTask, delayTask);

                    if (readTask != completedTask) break;

                    var item = await readTask;
                    try
                    {
                        if (_options.StateCallbackMode == StateCallbackMode.Async)
                        {
                            await Task.Run(() => OnStateChanged?.Invoke(item.state, item.message), _lifecycleCts.Token);
                        }
                        else
                        {
                            OnStateChanged?.Invoke(item.state, item.message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error in OnStateChanged user callback.");
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        public void Dispose()
        {
            try { Stop(); } catch { }
            try { _lifecycleCts.Cancel(); } catch { }
            try { _stateChannel.Writer.TryComplete(); } catch { }
            try { _stateLoopTask?.Wait(2000); } catch { }
            try { _lifecycleCts.Dispose(); } catch { }
            try { _sendLock.Dispose(); } catch { }
            try { _channel.Dispose(); } catch { }
        }

        private async Task<bool> TrySendInternalAsync(byte[] data)
        {
            if (CurrentState != GuardState.Connected) return false;

            try
            {
                if (!await _sendLock.WaitAsync(_options.SendLockTimeoutMs, _lifecycleCts.Token))
                {
                    _logger.Warn("Send timeout: could not acquire send lock.");
                    return false;
                }
            }
            catch (OperationCanceledException) { return false; }
            catch (ObjectDisposedException) { return false; }

            try
            {
                if (CurrentState != GuardState.Connected) return false;
                using var timeoutCts = new CancellationTokenSource(_options.SendTimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token, timeoutCts.Token);
                await _channel.SendAsync(data, linkedCts.Token);
                _lastSendTime = DateTime.Now;
                _hasSentSinceConnect = true;
                return true;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && !_lifecycleCts.IsCancellationRequested)
                {
                    _logger.Warn("Send timeout: forcing reconnect.");
                    TriggerReconnect();
                    return false;
                }
                _logger.Error(ex, "Send failed, triggering reconnect.");
                TriggerReconnect();
                return false;
            }
            finally
            {
                try { _sendLock.Release(); } catch { }
            }
        }

        private bool ShouldCheckWatchdog(DateTime now)
        {
            if (_options.WatchdogWarmupMs > 0
                && (now - _connectedAt).TotalMilliseconds < _options.WatchdogWarmupMs)
            {
                return false;
            }

            if (_options.WatchdogRequiresSend && !_hasSentSinceConnect)
            {
                return false;
            }

            return true;
        }
    }
}
