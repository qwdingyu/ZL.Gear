using System.Collections.Generic;

namespace ZL.Gear.Communication.Abstractions
{
    /// <summary>
    /// 协议无关的分帧器：把流式字节切成一帧帧。
    /// 约定：ExtractFrames() 只返回"完整帧"，残缺数据留在内部缓冲，等待后续 Append() 补齐。
    /// </summary>
    public interface IFrameSplitter
    {
        /// <summary>
        /// 追加数据到内部缓冲区
        /// </summary>
        /// <param name="buffer">数据源</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="count">数据长度</param>
        void Append(byte[] buffer, int offset, int count);

        /// <summary>
        /// 提取所有完整帧
        /// </summary>
        /// <returns>帧列表</returns>
        IList<System.ReadOnlyMemory<byte>> ExtractFrames();

        /// <summary>
        /// 重置分帧器状态
        /// </summary>
        void Reset();
    }
}
