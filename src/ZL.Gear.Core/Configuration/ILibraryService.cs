using System.Collections.Generic;

namespace ZL.Gear.Core.Configuration
{
    /// <summary>
    /// 提供项目库配置、路径管理和生命周期维护的核心接口。
    /// 取代旧的静态 ProjectLibraryManager。
    /// </summary>
    public interface ILibraryService
    {
        /// <summary>
        /// 已加载并可用的所有库名称
        /// </summary>
        IReadOnlyList<string> AvailableLibraries { get; }

        /// <summary>
        /// 当前激活的库名称
        /// </summary>
        string CurrentLibrary { get; set; }

        /// <summary>
        /// 获取当前激活库的配置对象
        /// </summary>
        LibraryConfig CurrentLibraryConfig { get; }

        /// <summary>
        /// 库是否已初始化并准备就绪
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 初始化库管理系统。扫描指定的项目根目录。
        /// </summary>
        /// <param name="projectRoot">项目根目录路径</param>
        /// <param name="defaultLibrary">默认激活的库，为空则取第一个</param>
        void Initialize(string projectRoot, string defaultLibrary = null);

        /// <summary>
        /// 获取指定库的配置对象
        /// </summary>
        LibraryConfig GetLibraryConfig(string libraryName);
        
        /// <summary>
        /// 获取当前激活库的完整物理路径
        /// </summary>
        string CurrentLibraryPath { get; }
    }
}
