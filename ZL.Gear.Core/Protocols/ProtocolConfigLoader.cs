using System;
using System.IO;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace ZL.Gear.Core.Protocols
{
    public static class ProtocolConfigLoader
    {
        private static readonly ConcurrentDictionary<string, ProtocolConfig> _cache = new ConcurrentDictionary<string, ProtocolConfig>();

        /// <summary>
        /// 加载协议配置。
        /// 1. 优先从缓存加载。
        /// 2. 尝试从 AppDomain.BaseDirectory/Protocols/{protocolName}.json 加载。
        /// 3. 尝试从 AppDomain.BaseDirectory/{protocolName}.json 加载。
        /// </summary>
        public static ProtocolConfig Load(string protocolName)
        {
            if (string.IsNullOrEmpty(protocolName)) return null;

            if (_cache.TryGetValue(protocolName, out var config)) return config;

            string fileName = protocolName.EndsWith(".json") ? protocolName : $"{protocolName}.json";
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            string[] searchPaths = new[]
            {
                Path.Combine(baseDir, "Protocols", fileName),
                Path.Combine(baseDir, fileName),
                // 开发环境调试: 尝试向上查找
                Path.Combine(baseDir, "../../../docs/Protocols", fileName) 
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        var loaded = JsonConvert.DeserializeObject<ProtocolConfig>(json);
                        if (loaded != null)
                        {
                            _cache.TryAdd(protocolName, loaded);
                            return loaded;
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException($"Failed to parse protocol config at {path}: {ex.Message}", ex);
                    }
                }
            }

            throw new FileNotFoundException($"Protocol config '{protocolName}' not found in search paths.");
        }
    }
}
