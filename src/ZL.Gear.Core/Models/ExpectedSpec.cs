using System;

namespace ZL.Gear.Core.Models
{

    public sealed class ExpectedSpec : ICloneable
    {
        public string Key { get; set; } = "default";
        /// <summary>
        /// 判定模式（与 <see cref="ZL.Gear.Engine.Evaluation.ResultEvaluator"/> switch 对齐 · 共 16 种）：
        /// 数值：range, equals, lcl_only, ucl_only, less_lcl, less_ucl, big_lcl, big_ucl；
        /// 布尔/位：bool, mask, bit_set；
        /// 字符串：regex, string_equals, contains, not_contains, has_value。
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
