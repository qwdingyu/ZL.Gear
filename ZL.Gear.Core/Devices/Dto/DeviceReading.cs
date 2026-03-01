namespace ZL.Gear.Core.Devices
{
    /// <summary>
    /// 代表从设备层返回的原始、标准化的读数。
    /// 这是 IDevice 接口的统一返回类型，是一个纯粹的数据传输对象 (DTO)。
    /// </summary>
    public record DeviceReading
    {
        public bool Success { get; set; }
        public object? Value { get; set; } // 原始值 (double, string, byte[], etc.)
        public string Message { get; set; } = string.Empty;
        public int SamplesCollected { get; set; }
        public static DeviceReading Succeeded(object? value, int samplesCollected = 1, string message = "OK")
            => new() { Success = true, Value = value, SamplesCollected = samplesCollected, Message = message };
        public static DeviceReading Succeeded(string message = "OK")
            => new() { Success = true, Value = "", SamplesCollected = 1, Message = message };

        public static DeviceReading Failed(object? value, string message)
            => new() { Success = false, Value = value, SamplesCollected = 1, Message = message };
        public static DeviceReading Failed(object? value, int sampleCount = 1, string message = "NG")
            => new() { Success = false, Value = value, SamplesCollected = sampleCount, Message = message };
        public static DeviceReading Failed(string errorMessage)
            => new() { Success = false, Message = errorMessage };
        /// <summary>
        /// 辅助方法，安全地获取强类型值，避免调用方进行类型转换。
        /// </summary>
        public T? GetValue<T>() => Value is T val ? val : default;
    }   
}
