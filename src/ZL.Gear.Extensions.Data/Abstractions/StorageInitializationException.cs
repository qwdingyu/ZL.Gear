using System;

namespace ZL.Gear.Extensions.Data.Abstractions
{
    /// <summary>
    /// 存储提供者初始化失败时抛出；宿主不得在未初始化成功的 Provider 上继续写入。
    /// </summary>
    public sealed class StorageInitializationException : Exception
    {
        public StorageInitializationException(string message)
            : base(message)
        {
        }

        public StorageInitializationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
