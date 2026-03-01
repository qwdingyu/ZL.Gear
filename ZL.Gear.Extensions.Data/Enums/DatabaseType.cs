namespace ZL.Gear.Extensions.Data.Enums
{
    /// <summary>
    /// 支持的数据库类型
    /// </summary>
    public enum DatabaseType
    {
        /// <summary>
        /// SQLite（嵌入式）
        /// </summary>
        SQLite,

        /// <summary>
        /// SQL Server
        /// </summary>
        SqlServer,

        /// <summary>
        /// MySQL / MariaDB
        /// </summary>
        MySql,

        /// <summary>
        /// PostgreSQL
        /// </summary>
        PostgreSql,

        /// <summary>
        /// Oracle
        /// </summary>
        Oracle,

        /// <summary>
        /// 达梦数据库（国产）
        /// </summary>
        DaMeng,

        /// <summary>
        /// 人大金仓（国产）
        /// </summary>
        KingBase
    }
}
