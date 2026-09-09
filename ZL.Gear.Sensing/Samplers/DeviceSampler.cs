using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Sensing.Samplers
{
    /// <summary>
    /// 基于 IDevice 的通用设备采样器。
    /// 适用于任何实现了 IDevice 接口的硬件。
    /// </summary>
    public class DeviceSampler<T> : ISensorSampler
    {
        private readonly IDevice _device;
        private readonly string _command;
        private readonly Dictionary<string, object> _args;
        private readonly StepContext _context;
        private readonly TimeSpan _interval;
        
        private readonly Subject<ZL.Gear.Core.Models.Measurement> _output = new();
        private CancellationTokenSource _cts;
        private Task _loopTask;
        private readonly Action<string> _log;

        public string Name { get; }
        public IObservable<ZL.Gear.Core.Models.Measurement> DataStream => _output.AsObservable();

        public DeviceSampler(
            string name,
            IDevice device, 
            string command, 
            Dictionary<string, object> args,
            StepContext context,
            TimeSpan interval,
            Action<string> logger = null)
        {
            Name = name;
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _command = command;
            _args = args ?? new Dictionary<string, object>();
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _interval = interval;
            _log = logger ?? SensingLog.Default;
        }

        public void Start()
        {
            if (_loopTask != null) return;
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => SampleLoop(_cts.Token));
        }

        public void Stop()
        {
            if (_cts == null) return;

            _cts.Cancel();

            // 等待后台采样循环安全退出，避免 Stop() 返回后任务仍在写入 _output
            var task = _loopTask;
            _cts = null;
            _loopTask = null;

            if (task != null)
            {
                try
                {
                    // 最多等待 5 秒，避免异常卡死
                    task.Wait(5000);
                }
                catch (AggregateException ex)
                {
                    _log($"[DeviceSampler] 停止采样时发生异常: {ex.InnerException?.Message}");
                }
            }
        }

        private async Task SampleLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 调用设备的 ExecuteAsync
                    var reading = await _device.ExecuteAsync(_command, _args, _context).ConfigureAwait(false);
                    
                    if (reading.Success)
                    {
                        var val = reading.Value;
                        // 尝试转换为 T
                        T typedValue = default;
                        try 
                        { 
                            typedValue = (T)Convert.ChangeType(val, typeof(T)); 
                        } 
                        catch (Exception ex) 
                        {
                            _log($"[DeviceSampler] 类型转换失败: {val} -> {typeof(T).Name}, 错误: {ex.Message}");
                            typedValue = default;
                        }
                        
                        _output.OnNext(ZL.Gear.Core.Models.Measurement<T>.Create(Name, typedValue, true));
                    }
                    else
                    {
                         _output.OnNext(ZL.Gear.Core.Models.Measurement.Create(Name, null, false, reading.Message));
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _output.OnNext(ZL.Gear.Core.Models.Measurement.Create(Name, null, false, ex.Message));
                }

                if (_interval > TimeSpan.Zero)
                {
                    try { await Task.Delay(_interval, token).ConfigureAwait(false); }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _log($"[DeviceSampler] 采样延迟异常: {ex.Message}");
                        break;
                    }
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _output.Dispose();
        }
    }
}
