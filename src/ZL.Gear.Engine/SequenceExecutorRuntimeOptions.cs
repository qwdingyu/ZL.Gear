namespace ZL.Gear.Engine
{
    /// <summary>
    /// SequenceExecutor 运行期选项（Builder 注入 DI）。
    /// </summary>
    public sealed class SequenceExecutorRuntimeOptions
    {
        /// <summary>
        /// 每次 Run 开始前清空本 Runtime 的设备隔离表。
        /// 默认 false（超时隔离持续到显式 Release/Clear）；产线复检工位可设为 true。
        /// </summary>
        public bool ClearDeviceQuarantineOnRunStart { get; set; }
    }
}
