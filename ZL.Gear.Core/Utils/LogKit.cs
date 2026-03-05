using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace ZL.Gear.Core.Utils
{
    /// <summary>
    /// 日志工具类，支持 NLog 和 Microsoft.Extensions.Logging 双模式。
    /// </summary>
    public class LogKit
    {
        static IPEndPoint remoteIP = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9999);
        private static string SelfAppName;
        
        // Microsoft.Extensions.Logging 兼容层
        private static Microsoft.Extensions.Logging.ILogger _fallbackLogger = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger("LogKit");
        private static bool _useFallbackLogger = false;
        
        private static readonly Lazy<Logger> _loggerInstance = new Lazy<Logger>(() =>
        {
            string configFile = Path.Combine(Environment.CurrentDirectory, "NLog.config");
            if (!File.Exists(configFile))
            {
                // 如果不存在，使用默认配置
                LogManager.Configuration = CreateDefaultConfiguration();
            }

            //System.Collections.Specialized.NameValueCollection appSetting = System.Configuration.ConfigurationManager.AppSettings;
            //if (appSetting.AllKeys.Contains("BizTrace"))
            //    canBizTrace = appSetting["BizTrace"].ToUpper() == "TRUE" ? true : false;
            // 返回 NLog 的 Logger 实例
            return LogManager.GetCurrentClassLogger();
        });

        private static Logger LoggerInstance => _loggerInstance.Value;
        
        /// <summary>
        /// 设置 Microsoft.Extensions.Logging 兼容层。
        /// 调用此方法后，所有 LogKit 调用将转发到指定的 ILogger。
        /// </summary>
        /// <param name="logger">ILogger 实例。</param>
        public static void SetLogger(Microsoft.Extensions.Logging.ILogger logger)
        {
            _fallbackLogger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger("LogKit");
            _useFallbackLogger = true;
        }
        
        /// <summary>
        /// 重置为使用 NLog 原生实现。
        /// </summary>
        public static void ResetToNLog()
        {
            _useFallbackLogger = false;
            _fallbackLogger = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger("LogKit");
        }


        static LogKit()
        {
            try
            {
                SelfAppName = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.ModuleName);
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
        }
        ~LogKit()
        {
        }
        static LoggingConfiguration CreateDefaultConfiguration()
        {
            var config = new LoggingConfiguration();
            //举例： maxArchiveDays = "7"
            //maxArchiveFiles = "10"
            //简要说明两个参数： maxArchiveDays: 7 保留七天内的日志、maxArchiveFiles = 10 保留文档最大数量为10。保留最近七天的十个日志文件，其他日志文件自动删除。

            //archiveAboveSize 以字节为单位的大小，超过该大小的日志文件将被自动归档
            //maxArchiveFiles: 应保留的存档文件的最大数量，如果maxArchiveFiles小于或等于0，则不删除旧文件；默认值为0；
            //maxArchiveDays: 应保留的存档文件的最大期限。当 archiveNumbering 为Rolling时无效；如果maxArchiveDays小于或等于0，咋不删除旧的归档文件；默认值为0；
            // 定义默认的文件日志目标
            var fileTarget = new FileTarget("file")
            {
                FileName = "${basedir}/logs/${shortdate}/${event-properties:item=LogFileName}.log",
                ArchiveFileName = "${basedir}/logs/${shortdate}/archive/${event-properties:item=LogFileName}.{#}.log",
                ArchiveAboveSize = 1024 * 1024 * 5,
                //ArchiveNumbering = ArchiveNumberingMode.Sequence,
                //MaxArchiveFiles = 0, //不删除归档文件
                Layout = "${longdate} ${level} ${message}"
            };
            config.AddTarget(fileTarget);
            config.AddRuleForAllLevels(fileTarget);

            //// 定义默认的网络日志目标
            //var networkTarget = new NetworkTarget("network")
            //{
            //    Address = "udp://127.0.0.1:2012",
            //    NewLine = false,
            //    MaxMessageSize = 65000,
            //    Encoding = System.Text.Encoding.GetEncoding("gbk"),
            //    Layout = "${machinename}- ${local-ip}- | ${message}- ${exception}"
            //};
            //config.AddTarget(networkTarget);
            //config.AddRuleForAllLevels(networkTarget);

            return config;
        }


        //决定日志文件的名称，如果提供了自定义前缀，则使用前缀和日志级别
        private static string DetermineLogFileName(NLog.LogLevel logLevel, string customPrefix = null)
        {
            return string.IsNullOrEmpty(customPrefix) ? $"{SelfAppName}_{logLevel}" : $"{SelfAppName}_{customPrefix}_{logLevel}";
        }

        //private static string DetermineLogFileName(LogLevel logLevel, string customPrefix = null)
        //{
        //    return string.IsNullOrEmpty(customPrefix) ? logLevel.ToString() : customPrefix;
        //}

        /// <summary>
        /// 创建日志内容
        /// </summary>
        /// <param name="message"></param>
        /// <param name="logLevel"></param>
        /// <param name="customPrefix"></param>
        public static void WriteLogs(string message, string customPrefix = null)
        {
            if (_useFallbackLogger)
            {
                _fallbackLogger.LogInformation(customPrefix != null ? "[{Prefix}] {Message}" : "{Message}", customPrefix, message);
            }
            else
            {
                NLog.LogLevel logLevel = NLog.LogLevel.Info;
                WriteLogs(message, logLevel, customPrefix);
            }
        }
        
        public static void WriteAndTrace(string message, string customPrefix = null)
        {
            if (_useFallbackLogger)
            {
                _fallbackLogger.LogInformation(customPrefix != null ? "[{Prefix}] {Message}" : "{Message}", customPrefix, message);
            }
            else
            {
                NLog.LogLevel logLevel = NLog.LogLevel.Info;
                WriteLogs(message, logLevel, customPrefix);
            }
            //TraceKit.SendMsg(remoteIP, message);
        }

        public static void WriteLogs(string message, NLog.LogLevel logLevel, string customPrefix = null)
        {
            if (_useFallbackLogger)
            {
                var msLogLevel = ConvertLogLevel(logLevel);
                _fallbackLogger.Log(msLogLevel, "{Message}", message);
            }
            else
            {
                var logFileName = DetermineLogFileName(logLevel, customPrefix);
                var logEvent = new LogEventInfo(logLevel, "", message)
                {
                    Properties = { ["LogFileName"] = logFileName }
                };
                LoggerInstance.Log(logEvent);
            }
        }

        public static void Info(string message, string customPrefix = null)
        {
            if (_useFallbackLogger)
            {
                _fallbackLogger.LogInformation(customPrefix != null ? "[{Prefix}] {Message}" : "{Message}", customPrefix, message);
            }
            else
            {
                WriteLogs(message, NLog.LogLevel.Info, customPrefix);
            }
        }

        public static void Debug(string message, string customPrefix = null)
        {
            if (_useFallbackLogger)
            {
                _fallbackLogger.LogDebug(customPrefix != null ? "[{Prefix}] {Message}" : "{Message}", customPrefix, message);
            }
            else
            {
                WriteLogs(message, NLog.LogLevel.Debug, customPrefix);
            }
        }

        public static void Error(string message, string customPrefix = null)
        {
            if (_useFallbackLogger)
            {
                _fallbackLogger.LogError(customPrefix != null ? "[{Prefix}] {Message}" : "{Message}", customPrefix, message);
            }
            else
            {
                WriteLogs(message, NLog.LogLevel.Error, customPrefix);
            }
        }

        public static void Warn(string message, string customPrefix = null)
        {
            if (_useFallbackLogger)
            {
                _fallbackLogger.LogWarning(customPrefix != null ? "[{Prefix}] {Message}" : "{Message}", customPrefix, message);
            }
            else
            {
                WriteLogs(message, NLog.LogLevel.Warn, customPrefix);
            }
        }
        
        /// <summary>
        /// 转换 NLog LogLevel 到 Microsoft.Extensions.Logging LogLevel
        /// </summary>
        private static Microsoft.Extensions.Logging.LogLevel ConvertLogLevel(NLog.LogLevel nlogLevel)
        {
            if (nlogLevel == NLog.LogLevel.Trace || nlogLevel == NLog.LogLevel.Debug)
                return Microsoft.Extensions.Logging.LogLevel.Debug;
            if (nlogLevel == NLog.LogLevel.Info)
                return Microsoft.Extensions.Logging.LogLevel.Information;
            if (nlogLevel == NLog.LogLevel.Warn)
                return Microsoft.Extensions.Logging.LogLevel.Warning;
            if (nlogLevel == NLog.LogLevel.Error || nlogLevel == NLog.LogLevel.Fatal)
                return Microsoft.Extensions.Logging.LogLevel.Error;
            return Microsoft.Extensions.Logging.LogLevel.Information;
        }
    }
}