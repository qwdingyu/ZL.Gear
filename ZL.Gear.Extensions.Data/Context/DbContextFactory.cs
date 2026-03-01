using SqlSugar;
using System;
using ZL.Gear.Extensions.Data.Configuration;
using ZL.Gear.Extensions.Data.Enums;

namespace ZL.Gear.Extensions.Data.Context
{
    public static class DbContextFactory
    {
        public static ISqlSugarClient Create(DatabaseConfiguration config)
        {
            if (string.IsNullOrEmpty(config.ConnectionString))
            {
                throw new ArgumentException("连接字符串不能为空", nameof(config));
            }

            var dbType = ConvertToDbType(config.DbType);
            var connectionString = config.ConnectionString;

            if (config.DbType == DatabaseType.SQLite)
            {
                connectionString = EnableSqliteWalMode(connectionString);
            }

            var db = new SqlSugarClient(new ConnectionConfig()
            {
                ConnectionString = connectionString,
                DbType = dbType,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            });

            db.Ado.CommandTimeOut = config.CommandTimeout;

            return db;
        }

        public static ISqlSugarClient Create(string connectionString, DatabaseType dbType)
        {
            string finalConnectionString = connectionString;
            
            if (dbType == DatabaseType.SQLite && !connectionString.Contains("DataSource="))
            {
                finalConnectionString = $"DataSource={connectionString}";
            }
            
            return Create(new DatabaseConfiguration
            {
                DbType = dbType,
                ConnectionString = finalConnectionString
            });
        }

        public static ISqlSugarClient CreateSqlite(string filePath)
        {
            var connectionString = $"DataSource={filePath}";
            return Create(connectionString, DatabaseType.SQLite);
        }

        public static ISqlSugarClient CreateMySql(string server, int port, string database, string user, string password)
        {
            var connectionString = $"Server={server};Port={port};Database={database};User={user};Password={password};Charset=utf8mb4;";
            return Create(connectionString, DatabaseType.MySql);
        }

        public static ISqlSugarClient CreateSqlServer(string server, string database, string user, string password)
        {
            var connectionString = $"Server={server};Database={database};User Id={user};Password={password};";
            return Create(connectionString, DatabaseType.SqlServer);
        }

        private static DbType ConvertToDbType(DatabaseType type)
        {
            return type switch
            {
                DatabaseType.SQLite => DbType.Sqlite,
                DatabaseType.SqlServer => DbType.SqlServer,
                DatabaseType.MySql => DbType.MySql,
                _ => throw new NotSupportedException($"不支持的数据库类型: {type}")
            };
        }

        private static string EnableSqliteWalMode(string connectionString)
        {
            return connectionString;
        }
    }
}
