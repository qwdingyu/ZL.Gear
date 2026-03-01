namespace ZL.Gear.Sensing.Dto
{

    public enum ScenarioType
    {
        /// <summary>
        /// 独立的，不依赖任何其他步骤，也不被任何步骤依赖。
        /// </summary>
        Standalone,
        /// <summary>
        /// 主步骤，会发出开始信号，并控制从属步骤的结束。
        /// </summary>
        EventMaster,
        /// <summary>
        /// 从属步骤，等待主步骤的开始信号。
        /// </summary>
        EventDependent
    }
}
