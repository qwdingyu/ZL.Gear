using System;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// 产品扫码事件
    /// </summary>
    public class ProductBarcodeScannedEvent : BaseEvent
    {
        public string Barcode { get; }
        public string StationId { get; }
        public ProductBarcodeScannedEvent(string barcode, string stationId = "")
        {
            Barcode = barcode;
            StationId = stationId;
        }
    }

    /// <summary>
    /// 产品进站事件
    /// </summary>
    public class ProductEntryEvent : BaseEvent
    {
        public string Barcode { get; }
        public string StationId { get; }
        public ProductEntryEvent(string barcode, string stationId)
        {
            Barcode = barcode;
            StationId = stationId;
        }
    }

    /// <summary>
    /// 产品出站事件
    /// </summary>
    public class ProductExitEvent : BaseEvent
    {
        public string Barcode { get; }
        public string Result { get; } // OK/NG
        public ProductExitEvent(string barcode, string result)
        {
            Barcode = barcode;
            Result = result;
        }
    }

    /// <summary>
    /// 生产判定完成事件
    /// </summary>
    public class ProductDecisionEvent : BaseEvent
    {
        public string Barcode { get; }
        public bool IsPassed { get; }
        public string Remarks { get; }
        public ProductDecisionEvent(string barcode, bool isPassed, string remarks = "")
        {
            Barcode = barcode;
            IsPassed = isPassed;
            Remarks = remarks;
        }
    }
}
