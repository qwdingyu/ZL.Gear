using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine.Management
{
    /// <summary>
    /// 工位状态枚举
    /// </summary>
    public enum StationState
    {
        Offline,    // 离线/未初始化
        Idle,       // 空闲/待机/等待触发
        Working,    // 工作中/测试中
        Error,      // 故障/急停
        Paused      // 暂停
    }

    /// <summary>
    /// 工位管理器：负责管理设备的生命周期、触发信号监听以及状态流转。
    /// 它是连接外部世界（PLC信号/UI按钮）与 MicroWorkflow 的桥梁。
    /// </summary>
    public class StationManager
    {
        // 核心依赖
        private readonly SequenceExecutor _executor;
        private readonly IDeviceService _deviceService;
        private readonly Action<string> _log;

        // 状态管理
        private volatile StationState _currentState = StationState.Offline;
        public StationState CurrentState => _currentState;

        // 触发源 (可以是 PLC 信号，也可以是扫码枪)
        private Func<CancellationToken, Task<bool>> _triggerCondition;

        // 全局取消令牌
        private CancellationTokenSource _stationCts;
        private Task _autoRunTask;

        public event Action<StationState> StateChanged;

        public StationManager(SequenceExecutor executor, IDeviceService deviceService, Action<string> log)
        {
            _executor = executor;
            _deviceService = deviceService;
            _log = log;
        }

        /// <summary>
        /// 配置触发条件 (例如：当 PLC 地址 D100 == 1 时)
        /// </summary>
        public void SetTrigger(Func<CancellationToken, Task<bool>> trigger)
        {
            _triggerCondition = trigger;
        }

        /// <summary>
        /// 启动工位自动运行循环
        /// </summary>
        public void StartAutoRun()
        {
            if (_currentState != StationState.Offline) return;

            _stationCts = new CancellationTokenSource();
            _autoRunTask = Task.Factory.StartNew(() => AutoRunLoop(_stationCts.Token), TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// 停止/下线
        /// </summary>
        public async Task StopAsync()
        {
            _stationCts?.Cancel();

            // 等待后台工位循环安全退出
            var task = _autoRunTask;
            _stationCts?.Dispose();
            _stationCts = null;
            _autoRunTask = null;

            if (task != null)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 正常取消
                }
                catch (Exception ex)
                {
                    _log($"[StationManager] 停止工位循环时发生异常: {ex.Message}");
                }
            }

            await SetStateAsync(StationState.Offline);
        }

        private async Task AutoRunLoop(CancellationToken token)
        {
            _log("工位自动化循环已启动。");

            // 1. 初始化所有设备
            try
            {
                await _deviceService.InitializeAllDevicesAsync(token: token).ConfigureAwait(false);
                await SetStateAsync(StationState.Idle).ConfigureAwait(false);
            }
            catch
            {
                await SetStateAsync(StationState.Error).ConfigureAwait(false);
                return;
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // --- IDLE 状态：等待触发 ---
                    if (_currentState == StationState.Idle)
                    {
                        // 检查触发信号 (例如：产品到位传感器 && 启动按钮)
                        if (_triggerCondition != null && await _triggerCondition(token).ConfigureAwait(false))
                        {
                            await SetStateAsync(StationState.Working).ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Delay(100, token).ConfigureAwait(false); // 避免 CPU 空转
                            continue;
                        }
                    }

                    // --- WORKING 状态：执行业务流程 ---
                    if (_currentState == StationState.Working)
                    {
                        _log("触发信号有效，开始执行流程...");

                        // 准备上下文 (Context)
                        var contextArgs = new Dictionary<string, object> { { "StartTime", DateTime.Now } };
                        // 可以在这里加载 StepConfigs
                        var steps = LoadStepsFromConfig();

                        // 执行核心引擎
                        var result = await _executor.ExecuteAsync(steps, "", "", contextArgs, token).ConfigureAwait(false);

                        // 业务后处理 (上传MES、打印等)
                        await HandleResultAsync(result).ConfigureAwait(false);

                        // 流程结束，回到 IDLE 等待下一个产品
                        // (或者如果出错，根据策略可能转入 Error 状态需人工复位)
                        await SetStateAsync(StationState.Idle).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log($"[工位异常] {ex.Message}");
                    await SetStateAsync(StationState.Error).ConfigureAwait(false);
                    // 在 Error 状态下，通常需要人工介入 (UI 点击复位按钮) 才能回到 Idle
                    while (_currentState == StationState.Error && !token.IsCancellationRequested)
                    {
                        await Task.Delay(500).ConfigureAwait(false);
                    }
                }
            }
        }

        // 外部 UI 调用的复位方法
        public async Task ResetErrorAsync()
        {
            if (_currentState == StationState.Error)
            {
                // 这里可以调用 DeviceManager.EmergencyStopAsync() 或者 ResetToSafeState()
                _log("人工复位故障...");
                await SetStateAsync(StationState.Idle).ConfigureAwait(false);
            }
        }

        private async Task SetStateAsync(StationState newState)
        {
            if (_currentState != newState)
            {
                _currentState = newState;
                StateChanged?.Invoke(newState);
                _log($"工位状态变更为: {newState}");
                await Task.Yield(); // 确保事件回调不阻塞主循环
            }
        }

        // 模拟方法
        private List<StepConfig> LoadStepsFromConfig() => new List<StepConfig>();
        private Task HandleResultAsync(TestRunResult result) => Task.CompletedTask;
    }
}
