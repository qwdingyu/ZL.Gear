using System.Collections.Generic;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Core.Configuration
{
    /// <summary>
    /// 负责缓存库的流程和模型配置。
    /// </summary>
    public class LibraryCacheService
    {
        // 缓存改为按库隔离
        private readonly Dictionary<string, Dictionary<string, ModelStepConfig>> _libraryFlowCache = new Dictionary<string, Dictionary<string, ModelStepConfig>>();
        private readonly Dictionary<string, Dictionary<string, ModelStepConfig>> _libraryModelStepsCache = new Dictionary<string, Dictionary<string, ModelStepConfig>>();

        public Dictionary<string, ModelStepConfig> GetFlowCache(string libraryName)
        {
            if (!_libraryFlowCache.ContainsKey(libraryName))
                _libraryFlowCache[libraryName] = new Dictionary<string, ModelStepConfig>();

            return _libraryFlowCache[libraryName];
        }

        public Dictionary<string, ModelStepConfig> GetModelStepsCache(string libraryName)
        {
            if (!_libraryModelStepsCache.ContainsKey(libraryName))
                _libraryModelStepsCache[libraryName] = new Dictionary<string, ModelStepConfig>();

            return _libraryModelStepsCache[libraryName];
        }

        public void ClearLibraryCache(string libraryName)
        {
            if (_libraryFlowCache.ContainsKey(libraryName))
                _libraryFlowCache[libraryName].Clear();

            if (_libraryModelStepsCache.ContainsKey(libraryName))
                _libraryModelStepsCache[libraryName].Clear();

            LogKit.Info($"已清理库 '{libraryName}' 的缓存");
        }

        public void ClearAllCache()
        {
            _libraryFlowCache.Clear();
            _libraryModelStepsCache.Clear();
            LogKit.Info("已清理所有库的缓存");
        }
    }
}
