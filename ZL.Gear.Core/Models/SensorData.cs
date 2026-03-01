namespace ZL.Gear.Core.Models
{
    using System;

    /// <summary>
    /// 座椅传感器数据
    /// </summary>
    [Obsolete("建议使用 Measurement.Metadata 存储此数据")]
    public class SensorValues
    {
        /// <summary>
        /// 座椅位置
        /// </summary>
        public int SeatPosition { get; set; }
        /// <summary>
        /// 靠背位置
        /// </summary>
        public int BackrestPosition { get; set; }
        /// <summary>
        /// 座垫位置
        /// </summary>
        public int CushionPosition { get; set; }
    }

    /// <summary>
    /// 传感器规格
    /// </summary>
    [Obsolete("建议使用 Measurement.Metadata 存储此数据")]
    public class SensorSpec
    {
        public string Name { get; set; }
        public int LCL { get; set; }
        public int UCL { get; set; }
        public string Unit { get; set; }
        public SensorSpec(string name, int lcl, int ucl) { Name = name; LCL = lcl; UCL = ucl; }
    }
}
