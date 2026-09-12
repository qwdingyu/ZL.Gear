namespace ZL.Gear.Sensing
{
    /// <summary>
    /// 实时采样/UI 推送端口（替代 Core TestEvents 直接依赖）。
    /// </summary>
    public interface IRealtimeSampleSink
    {
        void Publish(string stepKey, string channel, object value);
    }
}
