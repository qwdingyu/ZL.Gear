namespace ZL.Gear.Core.Models
{

    public enum RunTestMode
    {
        /// <summary>
        /// 包含启动条件，采样模式，采样周期，事件通知，结果验证等
        /// </summary>
        Auto,
        /// <summary>
        /// 手动测量，不关心结果，只关心实时值
        /// </summary>
        Manual
    }
}
