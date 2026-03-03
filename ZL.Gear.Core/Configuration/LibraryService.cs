using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Core.Configuration
{
    public class LibraryService : ILibraryService
    {
        private Dictionary<string, LibraryConfig> _libraryConfigs = new Dictionary<string, LibraryConfig>();
        private string _currentLibraryName;
        private string _projectRoot;
        private readonly ConfigurationLoader _loader = new ConfigurationLoader();

        public IReadOnlyList<string> AvailableLibraries => _libraryConfigs.Keys.ToList();

        public string CurrentLibrary
        {
            get => _currentLibraryName;
            set => SetCurrentLibrary(value);
        }

        public LibraryConfig CurrentLibraryConfig
        {
            get
            {
                if (string.IsNullOrEmpty(_currentLibraryName))
                    throw new InvalidOperationException("未设置当前激活库，请先调用 Initialize 或设置 CurrentLibrary。");
                
                return _libraryConfigs[_currentLibraryName];
            }
        }

        public string CurrentLibraryPath => string.IsNullOrEmpty(_currentLibraryName) ? null : Path.Combine(_projectRoot, _currentLibraryName);
        
        public bool IsInitialized => !string.IsNullOrEmpty(_projectRoot) && !string.IsNullOrEmpty(_currentLibraryName);

        public void Initialize(string projectRoot, string defaultLibrary = null)
        {
            _projectRoot = projectRoot;
            _libraryConfigs = _loader.LoadAllLibraries(projectRoot);

            if (_libraryConfigs.Count == 0)
            {
                LogKit.Warn($"在目录 {projectRoot} 下未找到任何有效的库定义。");
                return;
            }

            var target = defaultLibrary;
            if (string.IsNullOrEmpty(target) || !_libraryConfigs.ContainsKey(target))
            {
                target = _libraryConfigs.Keys.First();
            }

            SetCurrentLibrary(target);
        }

        public LibraryConfig GetLibraryConfig(string libraryName)
        {
            if (_libraryConfigs.TryGetValue(libraryName, out var config))
                return config;
            
            throw new ArgumentException($"库 '{libraryName}' 不存在于配置搜索路径中。");
        }

        private void SetCurrentLibrary(string libraryName)
        {
            if (!_libraryConfigs.ContainsKey(libraryName))
                throw new ArgumentException($"无法切换到库 '{libraryName}'，该库不存在。");

            _currentLibraryName = libraryName;
            LogKit.Info($"[LibraryService] 成功切换到项目库: {libraryName}");
        }
    }
}
