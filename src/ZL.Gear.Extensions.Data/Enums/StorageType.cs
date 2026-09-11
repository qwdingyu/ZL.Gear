namespace ZL.Gear.Extensions.Data.Enums
{
    /// <summary>
    /// 存储类型枚举
    /// </summary>
    public enum StorageType
    {
        /// <summary>
        /// 关系型数据库
        /// </summary>
        Database,

        /// <summary>
        /// CSV 文件
        /// </summary>
        CsvFile,

        /// <summary>
        /// 混合模式（同时写入数据库和 CSV）
        /// </summary>
        Mixed
    }
}
