using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using ZL.Gear.Core.Configuration;
using ZL.Gear.Core.Services;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Core
{
    public class GearProfileServices : IGearProfileService
    {
        private readonly ILibraryService _libraryService;
        
        public GearProfileServices(ILibraryService libraryService)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        }

        public Dictionary<string, object> LoadDeviceRoles()
        {
            if (!_libraryService.IsInitialized)
            {
                LogKit.Info("当前库未初始化，跳过加载设备角色配置。");
                return new Dictionary<string, object>();
            }

            var config = _libraryService.CurrentLibraryConfig;
            var path = config.DeviceProfilePath;
            
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                LogKit.Info($"设备角色配置文件【{path}】不存在。");
                return new Dictionary<string, object>();
            }
            
            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                LogKit.Error($"解析设备角色配置文件失败: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }
    }
}
