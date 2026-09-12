using System;
using System.Collections.Generic;
using System.Linq;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Core.Configuration
{
    /// <summary>
    /// 维护当前库的运行时上下文（状态）。
    /// </summary>
    public class LibraryContext
    {
        private readonly Dictionary<string, LibraryConfig> _libraryConfigs;
        private string _currentLibraryName;

        public LibraryContext(Dictionary<string, LibraryConfig> configs)
        {
            _libraryConfigs = configs ?? new Dictionary<string, LibraryConfig>();
        }

        public IReadOnlyList<string> AvailableLibraries => _libraryConfigs.Keys.ToList();

        public string CurrentLibraryName
        {
            get => _currentLibraryName;
            private set => _currentLibraryName = value;
        }

        public LibraryConfig CurrentLibraryConfig 
        {
            get
            {
                EnsureCurrentLibrary();
                return _libraryConfigs[_currentLibraryName];
            }
        }

        public void SetCurrentLibrary(string libraryName)
        {
            if (!_libraryConfigs.ContainsKey(libraryName))
                throw new ArgumentException($"库 '{libraryName}' 不存在");

            _currentLibraryName = libraryName;
            LogKit.Info($"当前库已切换到: {libraryName}");
        }

        public LibraryConfig GetLibraryConfig(string libraryName)
        {
            if (!_libraryConfigs.ContainsKey(libraryName))
                throw new ArgumentException($"库 '{libraryName}' 不存在");

            return _libraryConfigs[libraryName];
        }

        private void EnsureCurrentLibrary()
        {
             if (string.IsNullOrEmpty(_currentLibraryName) || !_libraryConfigs.ContainsKey(_currentLibraryName))
                throw new InvalidOperationException("未设置有效的当前库");
        }

        public string GetDefaultLibrary()
        {
            // 默认库：列表首项，或后续由 appsettings 指定
            return AvailableLibraries.FirstOrDefault(); 
        }
    }
}
