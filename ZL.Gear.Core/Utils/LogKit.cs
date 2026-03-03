using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace ZL.Gear.Core.Utils
{
    public class LogKit
    {
        static IPEndPoint remoteIP = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9999);
        private static string SelfAppName;
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
        private static string DetermineLogFileName(LogLevel logLevel, string customPrefix = null)
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
            LogLevel logLevel = LogLevel.Info;
            WriteLogs(message, logLevel, customPrefix);
        }
        public static void WriteAndTrace(string message, string customPrefix = null)
        {
            LogLevel logLevel = LogLevel.Info;
            WriteLogs(message, logLevel, customPrefix);
            //TraceKit.SendMsg(remoteIP, message);
        }

        public static void WriteLogs(string message, LogLevel logLevel, string customPrefix = null)
        {
            var logFileName = DetermineLogFileName(logLevel, customPrefix);
            var logEvent = new LogEventInfo(logLevel, "", message)
            {
                Properties = { ["LogFileName"] = logFileName }
            };
            LoggerInstance.Log(logEvent);
        }

        public static void Info(string message, string customPrefix = null)
        {
            WriteLogs(message, LogLevel.Info, customPrefix);
        }

        public static void Debug(string message, string customPrefix = null)
        {
            WriteLogs(message, LogLevel.Debug, customPrefix);
        }

        public static void Error(string message, string customPrefix = null)
        {
            WriteLogs(message, LogLevel.Error, customPrefix);
        }

        public static void Warn(string message, string customPrefix = null)
        {
            WriteLogs(message, LogLevel.Warn, customPrefix);
        }
    }
}