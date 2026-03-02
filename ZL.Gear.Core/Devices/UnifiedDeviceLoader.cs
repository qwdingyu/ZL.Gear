using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ZL.Gear.Core.Abstractions;

namespace ZL.Gear.Core.Devices
{
    /// <summary>
    /// 空日志实现（用于 Core 层无外部依赖场景）
    /// </summary>
    public sealed class NullDeviceLogger : IDeviceLogger
    {
        public static readonly IDeviceLogger Instance = new NullDeviceLogger();
        private NullDeviceLogger() { }
        public void Debug(string template, params object[] args) { }
        public void Info(string template, params object[] args) { }
        public void Warn(string template, params object[] args) { }
        public void Error(string template, params object[] args) { }
        public void Error(Exception exception, string template, params object[] args) { }
        public IDeviceLogger ForDevice(string deviceCode) => this;
    }

    /// <summary>
    /// 设备配置加载器实例（非静态）
    /// 
    /// 特点：
    /// 1. 无静态状态 - 支持多配置、热重载
    /// 2. 可测试 - 可以创建独立实例
    /// 3. 可观察 - 支持日志记录
    /// 
    /// 使用方式：
    /// ```csharp
    /// // 方式1：使用静态 facade（向后兼容）
    /// UnifiedDeviceLoader.Load("devices.json");
    /// var config = UnifiedDeviceLoader.Get("ResTester_1");
    /// 
    /// // 方式2：创建独立实例（测试场景）
    /// var loader = UnifiedDeviceLoader.CreateInstance();
    /// loader.Load("test/devices.json");
    /// var config = loader.Get("ResTester_1");
    /// ```
    /// </summary>
    public class UnifiedDeviceLoaderInstance
    {
        private System.Collections.Concurrent.ConcurrentDictionary<string, UnifiedDeviceConfig> _cache = 
            new System.Collections.Concurrent.ConcurrentDictionary<string, UnifiedDeviceConfig>(StringComparer.OrdinalIgnoreCase);
        
        private readonly object _loadLock = new object();
        private string? _userConfigPath;
        private readonly List<string> _embeddedAssemblies = new List<string>();
        private readonly IDeviceLogger _log;

        public UnifiedDeviceLoaderInstance(IDeviceLogger? logger = null)
        {
            _log = logger ?? NullDeviceLogger.Instance;
        }

        /// <summary>
        /// 注册包含嵌入配置的程序集
        /// </summary>
        /// <param name="assemblyName">程序集名称（如 "ZL.Gear.Drivers"）</param>
        public void RegisterEmbeddedAssembly(string assemblyName)
        {
            if (!_embeddedAssemblies.Contains(assemblyName))
            {
                _embeddedAssemblies.Add(assemblyName);
            }
        }

        /// <summary>
        /// 加载所有配置（内置 + 用户）
        /// 用户配置可覆盖内置配置
        /// </summary>
        /// <param name="userConfigPath">用户配置文件路径，为空则只加载内置配置</param>
        /// <returns>设备配置字典</returns>
        public System.Collections.Concurrent.ConcurrentDictionary<string, UnifiedDeviceConfig> Load(string? userConfigPath = null)
        {
            lock (_loadLock)
            {
                // 使用临时字典进行加载，加载完成后原子交换，保证读取线程的一致性
                var newCache = new System.Collections.Concurrent.ConcurrentDictionary<string, UnifiedDeviceConfig>(StringComparer.OrdinalIgnoreCase);
                _userConfigPath = userConfigPath;
                
                LoadEmbeddedConfigs(newCache);
                
                if (!string.IsNullOrEmpty(userConfigPath) && File.Exists(userConfigPath))
                {
                    LoadUserConfig(userConfigPath, newCache);
                }
                
                _cache = newCache;
                return _cache;
            }
        }

        /// <summary>
        /// 加载内置配置（从程序集嵌入资源）
        /// </summary>
        private void LoadEmbeddedConfigs(System.Collections.Concurrent.ConcurrentDictionary<string, UnifiedDeviceConfig> targetCache)
        {
            foreach (var assemblyName in _embeddedAssemblies)
            {
                try
                {
                    var assembly = Assembly.Load(assemblyName);
                    var resourceNames = assembly.GetManifestResourceNames();
                    
                    foreach (var resourceName in resourceNames)
                    {
                        if (resourceName.EndsWith(".devices.json", StringComparison.OrdinalIgnoreCase) ||
                            resourceName.EndsWith(".protocols.json", StringComparison.OrdinalIgnoreCase))
                        {
                            using var stream = assembly.GetManifestResourceStream(resourceName);
                            if (stream == null)
                            {
                                _log.Warn("无法获取资源流: {0}", resourceName);
                                continue;
                            }
                            using var reader = new StreamReader(stream);
                            var json = reader.ReadToEnd();
                            
                            var collection = JsonConvert.DeserializeObject<DeviceConfigCollection>(json);
                            if (collection?.Devices != null)
                            {
                                foreach (var device in collection.Devices)
                                {
                                    if (device.Enabled && !string.IsNullOrEmpty(device.Code))
                                    {
                                        targetCache[device.Code] = device;
                                    }
                                }
                                _log.Info("从 {0} 加载了 {1} 个设备", resourceName, collection.Devices.Count);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "无法加载嵌入程序集 {0}", assemblyName);
                }
            }
        }

        /// <summary>
        /// 加载用户配置（覆盖内置配置）
        /// </summary>
        private void LoadUserConfig(string configPath, System.Collections.Concurrent.ConcurrentDictionary<string, UnifiedDeviceConfig> targetCache)
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var collection = JsonConvert.DeserializeObject<DeviceConfigCollection>(json);
                
                if (collection?.Devices == null)
                {
                    return;
                }
                
                foreach (var device in collection.Devices)
                {
                    if (!string.IsNullOrEmpty(device.Code))
                    {
                        if (device.Enabled)
                        {
                            targetCache[device.Code] = device;
                        }
                        else if (targetCache.ContainsKey(device.Code))
                        {
                            targetCache.TryRemove(device.Code, out _);
                        }
                    }
                }
                _log.Info("从 {0} 加载了 {1} 个用户设备配置", configPath, collection.Devices.Count);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"加载用户配置文件失败: {configPath}", ex);
            }
        }

        /// <summary>
        /// 获取指定设备的配置
        /// </summary>
        public UnifiedDeviceConfig? Get(string deviceCode)
        {
            if (_cache.Count == 0)
            {
                Load();
            }
            return _cache.TryGetValue(deviceCode, out var config) ? config : null;
        }

        /// <summary>
        /// 获取所有已加载的设备配置
        /// </summary>
        public IReadOnlyDictionary<string, UnifiedDeviceConfig> GetAll()
        {
            if (_cache.IsEmpty)
            {
                Load(_userConfigPath);
            }
            return _cache;
        }

        /// <summary>
        /// 重新加载配置（用于热更新）
        /// </summary>
        public void Reload()
        {
            if (!string.IsNullOrEmpty(_userConfigPath))
            {
                Load(_userConfigPath);
            }
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
        }

        /// <summary>
        /// 获取已注册的嵌入程序集列表
        /// </summary>
        public IReadOnlyList<string> GetRegisteredAssemblies() => _embeddedAssemblies.AsReadOnly();
    }

    /// <summary>
    /// 统一设备配置加载器（静态 facade）
    /// 
    /// 向后兼容：内部使用默认实例，支持静态方法调用
    /// </summary>
    public static class UnifiedDeviceLoader
    {
        private static readonly UnifiedDeviceLoaderInstance _instance = new UnifiedDeviceLoaderInstance();

        public static void RegisterEmbeddedAssembly(string assemblyName) 
            => _instance.RegisterEmbeddedAssembly(assemblyName);

        public static System.Collections.Concurrent.ConcurrentDictionary<string, UnifiedDeviceConfig> Load(string? userConfigPath = null) 
            => _instance.Load(userConfigPath);

        public static UnifiedDeviceConfig? Get(string deviceCode) 
            => _instance.Get(deviceCode);

        public static IReadOnlyDictionary<string, UnifiedDeviceConfig> GetAll() 
            => _instance.GetAll();

        public static void Reload() => _instance.Reload();
        
        public static void Clear() => _instance.Clear();

        /// <summary>
        /// 创建新的加载器实例（用于测试场景）
        /// </summary>
        /// <param name="logger">可选的日志器</param>
        /// <returns>新的加载器实例</returns>
        public static UnifiedDeviceLoaderInstance CreateInstance(IDeviceLogger? logger = null)
        {
            return new UnifiedDeviceLoaderInstance(logger);
        }
    }
}
