using System;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 提供简单的线程安全单例持有者。
    /// </summary>
    /// <typeparam name="T">要持有的接口或类类型。</typeparam>
    public static class Singleton<T> where T : class
    {
        private static T _instance;
        private static readonly object _lock = new object();

        public static T Instance
        {
            get => _instance;
            set
            {
                lock (_lock)
                {
                    _instance = value;
                }
            }
        }
    }
}
