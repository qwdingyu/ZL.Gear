using System;
using System.Collections.Generic;
using System.Text;

namespace ZL.Gear.Communication.Framing
{
    #region 抽象定义

    /// <summary>
    /// 分帧器类型枚举
    /// </summary>
    public enum SplitterType
    {
        /// <summary>
        /// 固定长度分帧
        /// </summary>
        FixedLength,
        /// <summary>
        /// 分隔符分帧 (如 \r\n)
        /// </summary>
        Delimiter,
        /// <summary>
        /// 长度字段分帧 (头部包含长度信息)
        /// </summary>
        LengthField,
        /// <summary>
        /// Modbus RTU 专用分帧 (基于功能码推算 + CRC校验)
        /// </summary>
        ModbusRtu
    }

    /// <summary>
    /// 协议无关的分帧器接口
    /// 把流式字节切成完整的一帧帧数据。
    /// </summary>
    public interface IFrameSplitter
    {
        /// <summary>
        /// 向分帧器内部缓冲区追加数据
        /// </summary>
        void Append(byte[] data, int offset, int count);

        /// <summary>
        /// 尝试提取所有已接收完整的帧
        /// </summary>
        /// <returns>完整帧的列表，如果数据不足则返回空列表</returns>
        IList<ReadOnlyMemory<byte>> ExtractFrames();

        /// <summary>
        /// 重置分帧器状态（清空缓冲区）
        /// </summary>
        void Reset();
    }

    #endregion

    #region 工厂类

    /// <summary>
    /// 分帧器工厂
    /// </summary>
    public static class FrameSplitterFactory
    {
        /// <summary>
        /// 通用创建方法
        /// </summary>
        /// <param name="type">分帧器类型</param>
        /// <param name="param">参数（定长长度 或 长度字段偏移量）</param>
        /// <param name="delimiter">分隔符（仅用于 Delimiter 类型）</param>
        public static IFrameSplitter Create(SplitterType type, int param = 0, byte delimiter = (byte)'\n')
        {
            return type switch
            {
                SplitterType.FixedLength => new FixedLengthSplitter(param),
                SplitterType.Delimiter => new DelimiterSplitter(delimiter),
                SplitterType.ModbusRtu => new ModbusRtuSplitter(),
                SplitterType.LengthField => new LengthFieldSplitter(param), // param 作为 lengthFieldOffset
                _ => throw new NotSupportedException($"不支持的分帧器类型: {type}")
            };
        }

        public static IFrameSplitter CreateDelimiterSplitter(string delimiter) => new DelimiterSplitter(delimiter);

        public static IFrameSplitter CreateLengthFieldSplitter(int lengthFieldOffset, int lengthFieldSize = 2, bool littleEndian = false)
            => new LengthFieldSplitter(lengthFieldOffset, lengthFieldSize, littleEndian);
    }

    #endregion

    #region 基础缓冲抽象

    /// <summary>
    /// 包含基础缓冲区管理的抽象基类
    /// 设计目的：高性能，避免 List&lt;byte&gt; 的频繁扩容和 RemoveRange 造成的内存搬运
    /// </summary>
    public abstract class BaseSplitter : IFrameSplitter
    {
        protected byte[] _buffer;
        protected int _writeIndex; // 写入游标
        protected int _readIndex;  // 读取游标
        protected const int MAX_BUFFER_SIZE = 10 * 1024 * 1024; // 10MB 熔断保护

        protected BaseSplitter(int initialCapacity = 4096)
        {
            _buffer = new byte[initialCapacity];
        }

        public void Append(byte[] data, int offset, int count)
        {
            if (count <= 0) return;

            // 检查溢出
            if (_writeIndex + count > MAX_BUFFER_SIZE)
            {
                Reset(); // 丢弃旧数据，保护内存
                throw new InvalidOperationException($"分帧器缓冲区溢出 (>{MAX_BUFFER_SIZE} bytes)，可能是协议不匹配或数据流异常");
            }

            EnsureCapacity(count);
            Buffer.BlockCopy(data, offset, _buffer, _writeIndex, count);
            _writeIndex += count;
        }

        public abstract IList<ReadOnlyMemory<byte>> ExtractFrames();

        public virtual void Reset()
        {
            _writeIndex = 0;
            _readIndex = 0;
        }

        /// <summary>
        /// 确保容量足够，不足时扩容，或者通过整理碎片腾出空间
        /// </summary>
        private void EnsureCapacity(int appendSize)
        {
            int remainingSpace = _buffer.Length - _writeIndex;
            if (remainingSpace >= appendSize) return;

            int availableData = _writeIndex - _readIndex;

            // 策略1：如果去掉已读数据后，空间足够，则整理内存（搬运）
            if (_buffer.Length - availableData >= appendSize)
            {
                if (availableData > 0)
                {
                    Buffer.BlockCopy(_buffer, _readIndex, _buffer, 0, availableData);
                }
                _writeIndex = availableData;
                _readIndex = 0;
                return;
            }

            // 策略2：扩容
            int newSize = _buffer.Length;
            while (newSize < availableData + appendSize)
            {
                newSize *= 2;
                if (newSize > MAX_BUFFER_SIZE) newSize = MAX_BUFFER_SIZE;
            }

            var newBuffer = new byte[newSize];
            if (availableData > 0)
            {
                Buffer.BlockCopy(_buffer, _readIndex, newBuffer, 0, availableData);
            }
            _buffer = newBuffer;
            _writeIndex = availableData;
            _readIndex = 0;
        }

        /// <summary>
        /// 整理缓冲区：将未处理的数据移到头部
        /// </summary>
        protected void CompactBuffer()
        {
            int available = _writeIndex - _readIndex;
            if (available == 0)
            {
                _writeIndex = 0;
                _readIndex = 0;
            }
            else if (_readIndex > 0) // 只有当readIndex不在0时才需要搬运
            {
                // 如果剩余数据量很小，且readIndex很大，才搬运，避免频繁搬运
                // 这里采用简单策略：每次Extract后都搬运，保证Append时空间最大
                Buffer.BlockCopy(_buffer, _readIndex, _buffer, 0, available);
                _writeIndex = available;
                _readIndex = 0;
            }
        }
    }

    #endregion

    #region 具体实现

    /// <summary>
    /// 定长分包器
    /// </summary>
    public class FixedLengthSplitter : BaseSplitter
    {
        private readonly int _frameSize;

        public FixedLengthSplitter(int frameSize) : base(Math.Max(frameSize * 10, 4096))
        {
            if (frameSize <= 0) throw new ArgumentException("Frame size must be greater than 0", nameof(frameSize));
            _frameSize = frameSize;
        }

        public override IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();

            while (_writeIndex - _readIndex >= _frameSize)
            {
                // 创建副本，确保返回的 Memory 是安全的独立数据
                var frameData = new byte[_frameSize];
                Buffer.BlockCopy(_buffer, _readIndex, frameData, 0, _frameSize);
                frames.Add(new ReadOnlyMemory<byte>(frameData));

                _readIndex += _frameSize;
            }

            CompactBuffer();
            return frames;
        }
    }

    /// <summary>
    /// 分隔符分包器 (支持单字节或多字节分隔符)
    /// </summary>
    public class DelimiterSplitter : BaseSplitter
    {
        private readonly byte[] _delimiter;

        public DelimiterSplitter(byte delimiter) : this(new[] { delimiter }) { }

        public DelimiterSplitter(string delimiter) : this(Encoding.ASCII.GetBytes(delimiter))
        {
            if (string.IsNullOrEmpty(delimiter)) throw new ArgumentException("Delimiter cannot be empty");
        }

        private DelimiterSplitter(byte[] delimiterBytes) : base()
        {
            _delimiter = delimiterBytes;
        }

        public override IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();

            while (true)
            {
                int count = _writeIndex - _readIndex;
                if (count < _delimiter.Length) break;

                int index = IndexOf(_buffer, _readIndex, count, _delimiter);
                if (index < 0) break;

                int frameLen = (index - _readIndex) + _delimiter.Length; // 包含分隔符

                var frameData = new byte[frameLen];
                Buffer.BlockCopy(_buffer, _readIndex, frameData, 0, frameLen);
                frames.Add(new ReadOnlyMemory<byte>(frameData));

                _readIndex += frameLen;
            }

            CompactBuffer();
            return frames;
        }

        private static int IndexOf(byte[] buffer, int offset, int count, byte[] pattern)
        {
            int limit = offset + count - pattern.Length;
            for (int i = offset; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// 长度字段分包器
    /// 适用于协议头中包含固定位置长度字段的协议
    /// </summary>
    public class LengthFieldSplitter : BaseSplitter
    {
        private readonly int _lengthFieldOffset;
        private readonly int _lengthFieldSize;
        private readonly bool _littleEndian;

        // 可选：长度调整值（例如长度字段只表示Body长，总长需要+Head长）
        // 这里简化实现，假设长度字段表示整帧长度，或者使用者自己处理offset

        public LengthFieldSplitter(int lengthFieldOffset, int lengthFieldSize = 2, bool littleEndian = false) : base()
        {
            if (lengthFieldOffset < 0) throw new ArgumentException("Offset must >= 0");
            if (lengthFieldSize != 1 && lengthFieldSize != 2 && lengthFieldSize != 4)
                throw new ArgumentException("Size must be 1, 2 or 4");

            _lengthFieldOffset = lengthFieldOffset;
            _lengthFieldSize = lengthFieldSize;
            _littleEndian = littleEndian;
        }

        public override IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();

            while (true)
            {
                int available = _writeIndex - _readIndex;
                // 1. 检查是否足够读取长度字段
                if (available < _lengthFieldOffset + _lengthFieldSize) break;

                // 2. 解析长度
                int totalLength = ReadLength(_buffer, _readIndex + _lengthFieldOffset);

                // 3. 简单校验长度合法性 (防止解析出负数或0导致死循环)
                if (totalLength <= 0)
                {
                    // 策略：如果长度非法，通常意味着协议错位。
                    // 安全做法是丢弃1字节，尝试重新对齐（滑动窗口）
                    _readIndex++;
                    continue;
                }

                // 4. 检查是否收到完整的一帧
                if (available < totalLength) break;

                // 5. 提取帧
                var frameData = new byte[totalLength];
                Buffer.BlockCopy(_buffer, _readIndex, frameData, 0, totalLength);
                frames.Add(new ReadOnlyMemory<byte>(frameData));

                _readIndex += totalLength;
            }

            CompactBuffer();
            return frames;
        }

        private int ReadLength(byte[] buffer, int index)
        {
            return _lengthFieldSize switch
            {
                1 => buffer[index],
                2 => _littleEndian
                    ? buffer[index] | (buffer[index + 1] << 8)
                    : (buffer[index] << 8) | buffer[index + 1],
                4 => _littleEndian
                    ? buffer[index] | (buffer[index + 1] << 8) | (buffer[index + 2] << 16) | (buffer[index + 3] << 24)
                    : (buffer[index] << 24) | (buffer[index + 1] << 16) | (buffer[index + 2] << 8) | buffer[index + 3],
                _ => 0
            };
        }
    }

    /// <summary>
    /// Modbus RTU 分帧器
    /// 智能识别：异常帧(5字节)、写响应帧(8字节)、读响应帧(3+N+2字节)
    /// 并进行 CRC16 校验
    /// </summary>
    public class ModbusRtuSplitter : BaseSplitter
    {
        public ModbusRtuSplitter() : base() { }

        public override IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();

            while (true)
            {
                int available = _writeIndex - _readIndex;
                // Modbus 最短帧为异常帧：Addr(1)+Func(1)+Code(1)+CRC(2) = 5字节
                if (available < 5) break;

                byte funcCode = _buffer[_readIndex + 1];
                int frameLen = 0;

                // --- 长度推算逻辑 ---

                // 1. 异常响应 (最高位为1)
                if ((funcCode & 0x80) != 0)
                {
                    frameLen = 5;
                }
                // 2. 读线圈/寄存器响应 (01, 02, 03, 04) -> 变长
                // 格式: Addr(1) + Func(1) + Bytes(1) + Data(N) + CRC(2)
                else if (funcCode == 0x01 || funcCode == 0x02 || funcCode == 0x03 || funcCode == 0x04)
                {
                    // 还没收到“字节数”字段，无法计算长度
                    if (available < 3) break;

                    int dataBytes = _buffer[_readIndex + 2];
                    frameLen = 3 + dataBytes + 2; // Head(2) + LenByte(1) + Data + CRC(2)
                }
                // 3. 写单个/多个响应 (05, 06, 0F, 10) -> 固定长度 8字节
                // 格式: Addr(1) + Func(1) + AddrHi + AddrLo + ValHi + ValLo + CRC(2)
                else if (funcCode == 0x05 || funcCode == 0x06 || funcCode == 0x0F || funcCode == 0x10)
                {
                    frameLen = 8;
                }
                // 4. 其他不常用的功能码，暂时按最小长度处理或者丢弃
                // 这里为了鲁棒性，如果不识别功能码，我们假设它是错位数据，滑动一字节
                else
                {
                    _readIndex++;
                    continue;
                }

                // --- 完整性检查 ---

                if (available < frameLen) break; // 数据不够，等待下次

                // --- CRC 校验 ---

                // 为了性能，不在这里Copy数组，直接在原buffer上算
                ushort calculatedCrc = Crc16Modbus(_buffer, _readIndex, frameLen - 2);
                ushort receivedCrc = (ushort)(_buffer[_readIndex + frameLen - 2] | (_buffer[_readIndex + frameLen - 1] << 8));

                if (calculatedCrc == receivedCrc)
                {
                    // 校验通过，提取帧
                    var frameData = new byte[frameLen];
                    Buffer.BlockCopy(_buffer, _readIndex, frameData, 0, frameLen);
                    frames.Add(new ReadOnlyMemory<byte>(frameData));

                    _readIndex += frameLen; // 成功消费
                }
                else
                {
                    // CRC校验失败
                    // 重要策略：不要直接丢弃整个frameLen长度，因为可能只是头错了（例如噪点多了一个字节）
                    // 正确做法是：只丢弃头部 1 个字节，尝试重新寻找帧头
                    _readIndex++;
                }
            }

            CompactBuffer();
            return frames;
        }

        /// <summary>
        /// 标准 Modbus CRC16 计算
        /// </summary>
        public static ushort Crc16Modbus(byte[] data, int offset, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[offset + i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return crc;
        }
    }

    #endregion
}