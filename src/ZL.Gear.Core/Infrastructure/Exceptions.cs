using System;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// ZL.Gear 平台基础异常类
    /// </summary>
    public class GearException : Exception
    {
        public string ErrorCode { get; }
        public GearException(string message, string errorCode = "GENERIC_ERROR") : base(message)
        {
            ErrorCode = errorCode;
        }

        public GearException(string message, Exception innerException, string errorCode = "GENERIC_ERROR") 
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// 通讯物理层/传输层异常
    /// </summary>
    public class TransportException : GearException
    {
        public TransportException(string message, Exception innerException = null) 
            : base(message, innerException, "TRANSPORT_ERROR") { }
    }

    /// <summary>
    /// 设备通讯或硬件故障相关异常
    /// </summary>
    public class DeviceException : GearException
    {
        public string DeviceId { get; }
        public DeviceException(string deviceId, string message) 
            : base($"设备 [{deviceId}] 异常: {message}", "DEVICE_ERROR")
        {
            DeviceId = deviceId;
        }
    }

    /// <summary>
    /// 操作超时异常
    /// </summary>
    public class OperationTimeoutException : GearException
    {
        public OperationTimeoutException(string message) : base(message, "TIMEOUT_ERROR") { }
    }

    /// <summary>
    /// 调度引擎或运行逻辑异常
    /// </summary>
    public class EngineException : GearException
    {
        public EngineException(string message) : base(message, "ENGINE_ERROR") { }
    }
}
