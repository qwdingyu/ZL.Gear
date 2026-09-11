using ZL.Gear.Extensions.Data.Enums;

namespace ZL.Gear.Extensions.Data.Configuration
{
    /// <summary>
    /// 存储配置 - 支持多数据库和 CSV 的统一配置
    /// </summary>
    public class StorageConfiguration
    {
        /// <summary>
        /// 存储类型
        /// </summary>
        public StorageType StorageType { get; set; } = StorageType.Database;

        /// <summary>
        /// 数据库配置
        /// </summary>
        public DatabaseConfiguration? Database { get; set; }

        /// <summary>
        /// CSV 文件配置
        /// </summary>
        public CsvConfiguration? Csv { get; set; }
    }

    /// <summary>
    /// 数据库连接配置
    /// </summary>
    public class DatabaseConfiguration
    {
        /// <summary>
        /// 数据库类型
        /// </summary>
        public DatabaseType DbType { get; set; } = DatabaseType.SQLite;

        /// <summary>
        /// 连接字符串
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用分表
        /// </summary>
        public bool EnableSplitTable { get; set; } = true;

        /// <summary>
        /// 分表策略
        /// </summary>
        public SplitStrategy SplitStrategy { get; set; } = SplitStrategy.Monthly;

        /// <summary>
        /// 连接池大小
        /// </summary>
        public int PoolSize { get; set; } = 50;

        /// <summary>
        /// 命令超时时间（秒）
        /// </summary>
        public int CommandTimeout { get; set; } = 30;
    }

    /// <summary>
    /// CSV 文件配置
    /// </summary>
    public class CsvConfiguration
    {
        /// <summary>
        /// CSV 文件存储目录
        /// </summary>
        public string Directory { get; set; } = "./Data/CSV";

        /// <summary>
        /// 文件名前缀
        /// </summary>
        public string FilePrefix { get; set; } = "TestResults";

        /// <summary>
        /// 每个文件的行数限制（0 表示不限制）
        /// </summary>
        public int MaxLinesPerFile { get; set; } = 100000;

        /// <summary>
        /// 是否启用压缩
        /// </summary>
        public bool EnableCompression { get; set; } = false;

        /// <summary>
        /// 分隔符
        /// </summary>
        public string Separator { get; set; } = ",";

        /// <summary>
        /// 编码
        /// </summary>
        public string Encoding { get; set; } = "UTF-8";
    }

    /// <summary>
    /// 分表策略
    /// </summary>
    public enum SplitStrategy
    {
        /// <summary>
        /// 按月分表
        /// </summary>
        Monthly,

        /// <summary>
        /// 按周分表
        /// </summary>
        Weekly,

        /// <summary>
        /// 按天分表
        /// </summary>
        Daily,

        /// <summary>
        /// 按年分表
        /// </summary>
        Yearly
    }
}
