using System;

namespace ZL.Gear.Core.Models
{

    public sealed class ExpectedSpec : ICloneable
    {
        public string Key { get; set; } = "default";
        /// <summary>
        /// 默认范围判断
        ///  "range", "equals", "lcl_only", "ucl_only", "string_equals", "contains", "has_value"
        /// </summary>
        public string Mode { get; set; } = "range";
        /// <summary>
        ///  Lower Control Limit (规格下限)
        /// </summary>
        public double? LCL { get; set; }
        /// <summary>
        /// Upper Control Limit (规格上限)--同时用于 "equals", "contains" 
        /// </summary>
        public double? UCL { get; set; }
        /// <summary>
        /// 偏移量（补偿）。仅参与数值判定（range/equals 等），不改写入库的原始 Measurement.Value。
        /// </summary>
        public double? Offset { get; set; }

        public double? Value { get; set; } = 0;
        public string Unit { get; set; }
        // 字符串比较
        public string StringValue { get; set; }
        public object Clone()
        {
            return (ExpectedSpec)this.MemberwiseClone();
        }
    }
}


/* 
"ExpectedResults": [
  {
    "Key": "FirmwareVersion",
    "Mode": "contains", // ÐÂÄ£Ê½
    "StringValue": "2023"
  }
]
"ExpectedResults": [
  {
    "Key": "SerialNumber",
    "Mode": "has_value" // ÐÂÄ£Ê½
  }
]
"ExpectedResults": [
  {
    "Key": "VoltageAC",
    "Mode": "range",
    "LCL": 210.5,
    "UCL": 230.5,
    "Unit": "V"
  }
]
 */
