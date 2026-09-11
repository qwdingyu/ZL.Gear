using System;

namespace ZL.Gear.Core.Abstractions
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum DeviceLogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// 设备日志服务接口
    /// 
    /// 设计原则：
    /// 1. 简单契约 - Core 层独立，不依赖外部日志库
    /// 2. 结构化 - 支持模板参数，输出到 StructuredLog
    /// 3. 设备上下文 - 支持 ForDevice() 绑定设备代码
    /// </summary>
    public interface IDeviceLogger
    {
        /// <summary>
        /// 调试日志
        /// </summary>
        void Debug(string template, params object[] args);

        /// <summary>
        /// 信息日志
        /// </summary>
        void Info(string template, params object[] args);

        /// <summary>
        /// 警告日志
        /// </summary>
        void Warn(string template, params object[] args);

        /// <summary>
        /// 错误日志
        /// </summary>
        void Error(string template, params object[] args);

        /// <summary>
        /// 错误日志（带异常）
        /// </summary>
        void Error(Exception exception, string template, params object[] args);

        /// <summary>
        /// 创建带设备上下文的日志器
        /// </summary>
        /// <param name="deviceCode">设备代码</param>
        /// <returns>新的日志器实例</returns>
        IDeviceLogger ForDevice(string deviceCode);
    }
}
