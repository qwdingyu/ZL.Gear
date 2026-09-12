namespace ZL.Gear.Sensing
{
    /// <summary>
    /// 实时采样/UI 推送端口（Demos 宿主接线 LegacyBridge · G1b-09）。
    /// </summary>
    public interface IRealtimeSampleSink
    {
        void Publish(string stepKey, string channel, object value);
    }
}
