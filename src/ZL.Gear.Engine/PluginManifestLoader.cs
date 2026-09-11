using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// 插件清单加载器
    /// </summary>
    public static class PluginManifestLoader
    {
        private const string ManifestFileName = "plugin.json";

        /// <summary>
        /// 当前引擎程序集版本（只读缓存）。
        /// </summary>
        private static readonly Version _engineVersion = Assembly.GetExecutingAssembly().GetName().Version;

        /// <summary>
        /// 从插件目录加载清单
        /// </summary>
        /// <param name="pluginDirectory">插件目录</param>
        /// <returns>插件清单，不存在或版本不兼容则返回 null</returns>
        public static PluginManifest Load(string pluginDirectory)
        {
            if (string.IsNullOrEmpty(pluginDirectory)) return null;

            var manifestPath = Path.Combine(pluginDirectory, ManifestFileName);
            if (!File.Exists(manifestPath)) return null;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonConvert.DeserializeObject<PluginManifest>(json);

                // 校验最小引擎版本：若插件要求版本高于当前引擎，则拒绝加载
                if (!string.IsNullOrEmpty(manifest?.MinEngineVersion))
                {
                    if (!Version.TryParse(manifest.MinEngineVersion, out var minVersion))
                    {
                        _log?.Invoke($"[PluginManifestLoader] 清单版本格式无效: {manifest.MinEngineVersion}, 插件目录: {pluginDirectory}");
                        return null;
                    }

                    if (_engineVersion < minVersion)
                    {
                        _log?.Invoke($"[PluginManifestLoader] 插件要求最低引擎版本 {minVersion}，当前引擎版本 {_engineVersion}，不兼容，跳过加载: {pluginDirectory}");
                        return null;
                    }
                }

                // 启动期校验：RequiredAssemblies 必须存在且可加载
                if (manifest?.RequiredAssemblies != null && manifest.RequiredAssemblies.Count > 0)
                {
                    foreach (var rel in manifest.RequiredAssemblies)
                    {
                        if (string.IsNullOrWhiteSpace(rel))
                        {
                            _log?.Invoke($"[PluginManifestLoader] RequiredAssemblies 包含空路径，插件目录: {pluginDirectory}");
                            return null;
                        }

                        var asmPath = Path.GetFullPath(Path.Combine(pluginDirectory, rel));
                        if (!File.Exists(asmPath))
                        {
                            _log?.Invoke($"[PluginManifestLoader] RequiredAssemblies 缺少依赖: {rel}，插件目录: {pluginDirectory}");
                            return null;
                        }

                        try
                        {
                            // 仅做加载探测，不强制保留 Assembly 实例
                            Assembly.LoadFrom(asmPath);
                        }
                        catch (Exception ex)
                        {
                            _log?.Invoke($"[PluginManifestLoader] RequiredAssemblies 加载失败: {rel}，插件目录: {pluginDirectory}，原因: {ex.Message}");
                            return null;
                        }
                    }
                }

                return manifest;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[PluginManifestLoader] 加载清单失败: {pluginDirectory}, {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 保存清单到插件目录
        /// </summary>
        /// <param name="manifest">插件清单</param>
        /// <param name="pluginDirectory">插件目录</param>
        public static void Save(PluginManifest manifest, string pluginDirectory)
        {
            if (manifest == null || string.IsNullOrEmpty(pluginDirectory)) return;

            try
            {
                var manifestPath = Path.Combine(pluginDirectory, ManifestFileName);
                var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                File.WriteAllText(manifestPath, json);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[PluginManifestLoader] 保存清单失败: {pluginDirectory}, {ex.Message}");
            }
        }

        private static Action<string> _log;

        public static void Initialize(Action<string> log)
        {
            _log = log;
        }
    }
}
