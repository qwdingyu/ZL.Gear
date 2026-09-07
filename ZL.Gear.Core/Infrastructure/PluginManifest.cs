using System;
using System.Collections.Generic;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 插件清单文件模型
    /// </summary>
    public class PluginManifest
    {
        /// <summary>
        /// 插件唯一标识
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 插件版本
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 插件描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 插件入口类型（实现 IGearExtension 的类型全名）
        /// </summary>
        public string EntryType { get; set; }

        /// <summary>
        /// 依赖的程序集列表
        /// </summary>
        public List<string> Dependencies { get; set; } = new List<string>();

        /// <summary>
        /// 依赖的插件列表
        /// </summary>
        public List<string> PluginDependencies { get; set; } = new List<string>();

        /// <summary>
        /// 插件命令前缀（用于命名空间隔离）
        /// </summary>
        public string CommandPrefix { get; set; }

        /// <summary>
        /// 最小引擎版本
        /// </summary>
        public string MinEngineVersion { get; set; }

        /// <summary>
        /// 启动期必须加载的程序集相对路径列表（如 libs/Newtonsoft.Json.dll）。
        /// 用于在加载插件主程序集前预先解析依赖，避免反射或序列化阶段出现类型加载失败。
        /// </summary>
        public List<string> RequiredAssemblies { get; set; } = new List<string>();
    }
}
