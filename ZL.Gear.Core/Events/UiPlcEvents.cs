using System;
using System.Collections.Generic;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Events
{
    public static class UiPlcEvents
    {
        // 桥接到 EventBus 的静态代理方法
        private static IEventBus _bus => GlobalEvents.Bus;

        public static Action<bool, bool, bool, PlcRunMode, bool, bool> OnPlcAutoManualChanged
        {
            get => (running, reset, test, mode, safe, ths) => _bus.Publish(new PlcAutoManualEvent(running, reset, test, (int)mode, safe, ths));
            set => _bus.Subscribe<PlcAutoManualEvent>(e => value?.Invoke(e.IsRunning, e.IsReset, e.IsTest, (PlcRunMode)e.Mode, e.IsSafe, e.TwoHandStart));
        }

        public static Action<float> OnPlcSbrLocationChanged
        {
            get => (loc) => _bus.Publish(new PlcSbrLocationEvent(loc));
            set => _bus.Subscribe<PlcSbrLocationEvent>(e => value?.Invoke(e.Location));
        }

        public static Action<SensorValues, Dictionary<string, object>> OnSensorDataeChanged
        {
            get => (sv, dict) => _bus.Publish(new SensorDataEvent(sv.SeatPosition, sv.BackrestPosition, sv.CushionPosition, dict));
            set => _bus.Subscribe<SensorDataEvent>(e => value?.Invoke(new SensorValues { SeatPosition = e.SeatPos, BackrestPosition = e.BackPos, CushionPosition = e.CushionPos }, e.IOStates));
        }

        // 其余暂存为普通委托或按需迁移...
        public static Action<bool> OnTestStartChanged { get; set; }
        public static Action<bool, int> OnAllStepOverChanged { get; set; }
        public static Action<int, string, bool> OnManualCtrlChanged { get; set; }
        public static Action<string, object> OnManualWriteChanged { get; set; }
        public static Action<Dictionary<string, float>> OnSbrSbrLocationListChanged { get; set; } // 修正名称冲突
        public static Action<bool> OnPlcRequestStopOrReTestChanged { get; set; }
        public static Action<bool> OnTwoHandStartChanged { get; set; }
        public static Action<string, string, short> OnSetSeatCodeChanged { get; set; }
        public static Action<int, int, int, bool, bool, bool, bool, bool, bool> OnPlcSbrSensorLocationChanged { get; set; }
        public static Action<short, short, short> OnMesSendPlcCoreCodeChanged { get; set; }
        public static Action<bool> OnManualPageOpenCloseChanged { get; set; }
    }
}
