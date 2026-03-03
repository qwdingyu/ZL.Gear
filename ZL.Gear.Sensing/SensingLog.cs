using System;
using System.Diagnostics;

namespace ZL.Gear.Sensing
{
    /// <summary>
    /// ZL.Gear.Sensing 模块的统一日志管理类。
    /// 
    /// 使用方式：
    /// 1. 全局配置（应用程序入口）：
    ///    SensingLog.Default = msg => LogKit.Info(msg, "Sensing");
    ///    SensingLog.Debug = msg => LogKit.Debug(msg, "Sensing");
    /// 
    /// 2. 组件内部使用：
    ///    _log = logger ?? SensingLog.Default;
    ///    _log("消息内容");
    /// 
    /// 3. 调用方传入（优先）：
    ///    var engine = new Engine(customLogger);
    /// </summary>
    public static class SensingLog
    {
        /// <summary>
        /// 默认日志操作，全局配置后自动使用。
        /// 默认为空操作，生产环境请配置 LogKit。
        /// </summary>
        public static Action<string> Default
        {
            get => _default;
            set => _default = value ?? (_ => { });
        }
        private static Action<string> _default = _ => { };

        /// <summary>
        /// 调试级别日志，仅在调试构建设置下生效。
        /// </summary>
        public static Action<string> Debug
        {
            get => _debug;
            set => _debug = value ?? (_ => { });
        }
        private static Action<string> _debug = msg => { System.Diagnostics.Debug.WriteLine($"[Sensing] {msg}"); };

        /// <summary>
        /// 信息级别日志。
        /// </summary>
        public static Action<string> Info
        {
            get => _info;
            set => _info = value ?? (_ => { });
        }
        private static Action<string> _info = _ => { };

        /// <summary>
        /// 警告级别日志。
        /// </summary>
        public static Action<string> Warn
        {
            get => _warn;
            set => _warn = value ?? (_ => { });
        }
        private static Action<string> _warn = _ => { };

        /// <summary>
        /// 错误级别日志。
        /// </summary>
        public static Action<string> Error
        {
            get => _error;
            set => _error = value ?? (_ => { });
        }
        private static Action<string> _error = _ => { };

        /// <summary>
        /// 便捷方法：调试日志。
        /// </summary>
        public static void DebugLog(string message) => Debug(message);

        /// <summary>
        /// 便捷方法：信息日志。
        /// </summary>
        public static void InfoLog(string message) => Info(message);

        /// <summary>
        /// 便捷方法：警告日志。
        /// </summary>
        public static void WarnLog(string message) => Warn(message);

        /// <summary>
        /// 便捷方法：错误日志。
        /// </summary>
        public static void ErrorLog(string message) => Error(message);

        /// <summary>
        /// 便捷方法：默认级别日志。
        /// </summary>
        public static void Log(string message) => Default(message);

        /// <summary>
        /// 初始化方法，用于应用程序启动时配置日志。
        /// 推荐在 Program.Main 或应用入口处调用一次。
        /// </summary>
        /// <param name="logger">日志操作，支持传入 LogKit.Info 等方法。</param>
        /// <param name="enableDebug">是否启用调试日志（写入 Debug.WriteLine）。</param>
        public static void Initialize(Action<string> logger, bool enableDebug = false)
        {
            Default = logger;
            if (enableDebug)
            {
                Debug = msg => System.Diagnostics.Debug.WriteLine($"[Sensing] {msg}");
            }
        }
    }
}
