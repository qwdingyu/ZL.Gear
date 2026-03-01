using SqlSugar;
using System;
using System.Collections.Generic;

namespace ZL.Gear.Extensions.Data.Entities
{
    [SugarTable("TestResults")]
    public class TestResultEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [SugarColumn(Length = 100)]
        public string Barcode { get; set; } = string.Empty;

        [SugarColumn(Length = 50)]
        public string StationNo { get; set; } = string.Empty;

        [SugarColumn(Length = 50)]
        public string Model { get; set; } = string.Empty;

        public DateTime TestStartTime { get; set; }

        public double TotalDurationSec { get; set; }

        [SugarColumn(Length = 20)]
        public string FinalResult { get; set; } = string.Empty;

        public bool IsTransmitted { get; set; }

        public DateTime? TransmitTime { get; set; }

        [SugarColumn(Length = 500, IsNullable = true)]
        public string TransmitMessage { get; set; }

        [SugarColumn(IsIgnore = true)]
        public List<ResultItemEntity> Items { get; set; } = new List<ResultItemEntity>();

        public static string GetTableName(DateTime testTime)
        {
            return $"TestResults_{testTime:yyyyMM}";
        }

        public static string GetDetailTableName(DateTime testTime)
        {
            return $"ResultItems_{testTime:yyyyMM}";
        }
    }

    [SugarTable("ResultItems")]
    public class ResultItemEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [SugarColumn(IsNullable = false)]
        public long TestResultsId { get; set; }

        public string StepKey { get; set; } = string.Empty;

        public string TestItem { get; set; } = string.Empty;

        [SugarColumn(Length = 500, IsNullable = true)]
        public string TestValue { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string LCL { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string UCL { get; set; }

        [SugarColumn(Length = 20, IsNullable = true)]
        public string Unit { get; set; }

        [SugarColumn(IsNullable = true)]
        public double? MetricValue { get; set; }

        [SugarColumn(Length = 20)]
        public string TestResult { get; set; } = string.Empty;

        public DateTime StepStartTime { get; set; }
    }
}
