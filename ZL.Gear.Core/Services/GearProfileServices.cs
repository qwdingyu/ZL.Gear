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
        private static readonly Lazy<GearProfileServices> _instance = new Lazy<GearProfileServices>(() => new GearProfileServices());
        public static GearProfileServices Instance => _instance.Value;

        private LibraryConfig libraryConfig;
        
        public GearProfileServices()
        {
            libraryConfig = ProjectLibraryManager.GetCurrentLibraryConfig();
        }

        public Dictionary<string, object> LoadDeviceRoles()
        {
            var path = libraryConfig.SeatProfilePath;
            if (!File.Exists(path))
            {
                LogKit.Info($"设备配置文件【{path}】不存在！");
                return new Dictionary<string, object>();
            }
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(path));
        }
    }
}

