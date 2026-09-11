namespace ZL.Gear.Core.Devices.Abstractions
{
    /// <summary>
    /// 设备宿主模式（组合根显式声明，对齐 docs/145 / docs/147）。
    /// </summary>
    public enum DeviceHostMode
    {
        /// <summary>未声明宿主；<see cref="ZL.Gear.Engine.SequenceExecutorBuilder.Build"/> 必须 throw。</summary>
        Unspecified = 0,

        /// <summary>纯逻辑 Demo；配方不得含 Target/AdditionalTargets。</summary>
        LogicOnly = 1,

        /// <summary>仪器化产线；须 <see cref="ZL.Gear.Engine.SequenceExecutorBuilder.AsInstrumentedHost"/> 注入真实设备服务。</summary>
        Instrumented = 2
    }
}
