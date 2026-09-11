using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ZL.Gear.Core.Protocols
{
    /// <summary>
    /// 协议配置加载器
    /// 
    /// 支持两种加载方式：
    /// 1. 嵌入资源（优先）：从已注册的程序集中加载
    /// 2. 文件路径（兜底）：从 AppDomain.BaseDirectory/Protocols/ 目录加载
    /// 
    /// 使用方式：
    /// ```csharp
    /// // 方式1：注册嵌入程序集（推荐，用于生产环境）
    /// ProtocolConfigLoader.RegisterEmbeddedAssembly("ZL.Gear.Drivers");
    /// var config = ProtocolConfigLoader.Load("Keithley2000");
    /// 
    /// // 方式2：直接从文件加载（兜底，用于测试或自定义配置）
    /// var config = ProtocolConfigLoader.LoadFromFile("path/to/Keithley2000.json");
    /// ```
    /// </summary>
    public static class ProtocolConfigLoader
    {
        private static readonly ConcurrentDictionary<string, ProtocolConfig> _cache = new ConcurrentDictionary<string, ProtocolConfig>();
        
        // 已注册的程序集列表
        private static readonly List<Assembly> _registeredAssemblies = new List<Assembly>();
        
        // 初始化锁
        private static readonly object _initLock = new object();

        /// <summary>
        /// 注册包含嵌入协议配置的程序集
        /// </summary>
        /// <param name="assemblyName">程序集名称</param>
        public static void RegisterEmbeddedAssembly(string assemblyName)
        {
            lock (_initLock)
            {
                try
                {
                    var assembly = Assembly.Load(assemblyName);
                    if (assembly != null && !_registeredAssemblies.Contains(assembly))
                    {
                        _registeredAssemblies.Add(assembly);
                        System.Diagnostics.Debug.WriteLine($"[ProtocolConfigLoader] 已注册程序集: {assemblyName}");
                    }
                }
                catch (Exception ex)
                {
                    // 记录错误但继续运行
                    System.Diagnostics.Debug.WriteLine($"[ProtocolConfigLoader] 无法加载程序集 {assemblyName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 注册包含嵌入协议配置的程序集
        /// </summary>
        /// <param name="assembly">程序集实例</param>
        public static void RegisterEmbeddedAssembly(Assembly assembly)
        {
            lock (_initLock)
            {
                if (assembly != null && !_registeredAssemblies.Contains(assembly))
                {
                    _registeredAssemblies.Add(assembly);
                }
            }
        }

        /// <summary>
        /// 加载协议配置（嵌入资源优先，文件路径兜底）
        /// </summary>
        /// <param name="protocolName">协议名称（可含或不含 .json 后缀）</param>
        /// <returns>协议配置</returns>
        public static ProtocolConfig Load(string protocolName)
        {
            if (string.IsNullOrEmpty(protocolName)) return null;

            // 规范化协议名称（移除 .json 后缀作为缓存 key）
            string cacheKey = protocolName.EndsWith(".json") 
                ? protocolName.Substring(0, protocolName.Length - 5) 
                : protocolName;

            if (_cache.TryGetValue(cacheKey, out var cachedConfig)) return cachedConfig;

            // 方式1: 尝试从嵌入资源加载
            var embeddedConfig = TryLoadFromEmbeddedResources(protocolName);
            if (embeddedConfig != null)
            {
                _cache.TryAdd(cacheKey, embeddedConfig);
                return embeddedConfig;
            }

            // 方式2: 从文件路径加载（兜底）
            var fileConfig = TryLoadFromFile(protocolName);
            if (fileConfig != null)
            {
                _cache.TryAdd(cacheKey, fileConfig);
                return fileConfig;
            }

            throw new FileNotFoundException(
                $"协议配置文件未找到: {protocolName}\n" +
                "已尝试的加载方式:\n" +
                "1. 嵌入资源（请确保已调用 RegisterEmbeddedAssembly 注册程序集）\n" +
                $"2. 文件路径: {AppDomain.CurrentDomain.BaseDirectory}Protocols/{protocolName}");
        }

        /// <summary>
        /// 尝试从嵌入资源加载协议配置
        /// </summary>
        private static ProtocolConfig? TryLoadFromEmbeddedResources(string protocolName)
        {
            string fileName = protocolName.EndsWith(".json") ? protocolName : $"{protocolName}.json";

            foreach (var assembly in _registeredAssemblies)
            {
                try
                {
                    var resourceNames = assembly.GetManifestResourceNames();
                    foreach (var resourceName in resourceNames)
                    {
                        // 匹配 Resources/xxx.json 或 xxx.json
                        if (resourceName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) ||
                            resourceName.EndsWith($".Resources.{fileName}", StringComparison.OrdinalIgnoreCase))
                        {
                            using var stream = assembly.GetManifestResourceStream(resourceName);
                            if (stream == null) continue;

                            using var reader = new StreamReader(stream);
                            var json = reader.ReadToEnd();
                            var config = JsonConvert.DeserializeObject<ProtocolConfig>(json);
                            
                            if (config != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ProtocolConfigLoader] 从嵌入资源加载: {resourceName}");
                                return config;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProtocolConfigLoader] 从程序集 {assembly.GetName().Name} 加载失败: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// 尝试从文件路径加载协议配置
        /// </summary>
        private static ProtocolConfig? TryLoadFromFile(string protocolName)
        {
            string fileName = protocolName.EndsWith(".json") ? protocolName : $"{protocolName}.json";
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(baseDir, "Protocols", fileName);

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                var config = JsonConvert.DeserializeObject<ProtocolConfig>(json);
                
                if (config != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProtocolConfigLoader] 从文件加载: {path}");
                }
                
                return config;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"协议配置文件格式错误: {path}", ex);
            }
        }

        /// <summary>
        /// 从指定文件路径加载协议配置（绕过缓存和嵌入资源）
        /// </summary>
        /// <param name="filePath">完整的文件路径</param>
        /// <returns>协议配置</returns>
        public static ProtocolConfig LoadFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            
            if (_cache.TryGetValue(fileName, out var cachedConfig)) return cachedConfig;

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"协议配置文件未找到: {filePath}");
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var config = JsonConvert.DeserializeObject<ProtocolConfig>(json);
                
                if (config == null)
                {
                    throw new InvalidDataException($"协议配置文件解析失败: {filePath}");
                }
                
                _cache.TryAdd(fileName, config);
                return config;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"协议配置文件格式错误: {filePath}", ex);
            }
        }

        /// <summary>
        /// 清除缓存（用于测试或配置热重载）
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }
    }
}
