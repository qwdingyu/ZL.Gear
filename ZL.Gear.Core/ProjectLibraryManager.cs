using System;
using System.Collections.Generic;
using System.IO;
using ZL.Gear.Core.Configuration;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core
{
    /// <summary>
    /// [已过时] 请使用 LibraryContext, ConfigurationLoader 和 LibraryCacheService。
    /// 此类仅作为兼容性外观模式保留。
    /// </summary>
    [Obsolete("这是一个上帝类，请改用 ZL.Gear.Core.Configuration 命名空间下的服务", false)]
    public static class ProjectLibraryManager
    {
        // 静态单例持有重构后的服务实例，以保持静态 API 兼容性
        private static LibraryContext _context;
        private static readonly ConfigurationLoader _loader = new ConfigurationLoader();
        private static readonly LibraryCacheService _cache = new LibraryCacheService();
        private static string _projectCfgPath;

        public static IReadOnlyList<string> AvailableLibraries => _context?.AvailableLibraries ?? new List<string>();

        public static void Initialize(string projectCfg, string defaultLibrary = null)
        {
            _projectCfgPath = projectCfg;
            var configs = _loader.LoadAllLibraries(projectCfg);
            _context = new LibraryContext(configs);
            
            var targetLib = defaultLibrary ?? _context.GetDefaultLibrary();
            if (targetLib != null)
            {
                _context.SetCurrentLibrary(targetLib);
            }
        }

        public static string CurrentLibrary 
        { 
            get => _context.CurrentLibraryName; 
            set => _context.SetCurrentLibrary(value); 
        }

        public static string CurrentLibraryPath => Path.Combine(_projectCfgPath, CurrentLibrary);
        
        // 映射属性
        public static string appConfigPath => _context.CurrentLibraryConfig.AppConfigPath;
        public static string appConfigDescriptorsPath => _context.CurrentLibraryConfig.AppConfigDescriptorsPath;
        public static string appsettingsJsonPath => _context.CurrentLibraryConfig.AppsettingsPath;
        public static string devicesJsonPath => _context.CurrentLibraryConfig.DevicesPath;
        public static string infraJsonPath => _context.CurrentLibraryConfig.InfrastructurePath;
        public static string barcodeRulesJsonPath => _context.CurrentLibraryConfig.BarcodeRulesPath;
        public static string modelListJsonPath => _context.CurrentLibraryConfig.ModelListPath;
        public static string seatProfileJsonPath => _context.CurrentLibraryConfig.SeatProfilePath;
        public static string stepCatalogJsonPath => _context.CurrentLibraryConfig.StepCatalogPath;

        // 缓存访问方法 - 委托给 CacheService
        public static Dictionary<string, ModelStepConfig> GetFlowCache(string libraryName)
            => _cache.GetFlowCache(libraryName);

        public static Dictionary<string, ModelStepConfig> GetModelStepsCache(string libraryName)
            => _cache.GetModelStepsCache(libraryName);

        public static void ClearLibraryCache(string libraryName)
            => _cache.ClearLibraryCache(libraryName);

        public static void ClearAllCache()
            => _cache.ClearAllCache();

        public static void SetCurrentLibrary(string libraryName)
            => _context.SetCurrentLibrary(libraryName);

        public static LibraryConfig GetCurrentLibraryConfig()
            => _context.CurrentLibraryConfig;

        public static LibraryConfig GetLibraryConfig(string libraryName)
            => _context.GetLibraryConfig(libraryName);
    }
}
