using System;

namespace ZL.Gear.Communication.Guard
{
    /// <summary>
    /// 日志接口
    /// </summary>
    public interface IGuardLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Error(Exception ex, string message);
    }

    /// <summary>
    /// 空日志实现
    /// </summary>
    public sealed class NullLogger : IGuardLogger
    {
        public static readonly IGuardLogger Instance = new NullLogger();
        private NullLogger() { }
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(Exception ex, string message) { }
    }

    /// <summary>
    /// 控制台日志实现
    /// </summary>
    public sealed class ConsoleLogger : IGuardLogger
    {
        private readonly string _prefix;

        public ConsoleLogger(string prefix = "Guard")
        {
            _prefix = prefix;
        }

        public void Debug(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [DEBUG] [{_prefix}] {message}");
        }

        public void Info(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [INFO ] [{_prefix}] {message}");
        }

        public void Warn(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [WARN ] [{_prefix}] {message}");
        }

        public void Error(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [ERROR] [{_prefix}] {message}");
        }

        public void Error(Exception ex, string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [ERROR] [{_prefix}] {message}");
            Console.WriteLine($"         Exception: {ex.Message}");
        }
    }
}
