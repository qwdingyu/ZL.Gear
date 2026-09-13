using System.Collections.Generic;
using System.IO;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Core.Configuration
{
    /// <summary>
    /// 负责从文件系统加载库配置，保持无状态。
    /// </summary>
    public class ConfigurationLoader
    {
        public Dictionary<string, LibraryConfig> LoadAllLibraries(string rootPath)
        {
             var configs = new Dictionary<string, LibraryConfig>();

            if (!Directory.Exists(rootPath)) 
            {
                LogKit.Info($"配置根目录不存在: {rootPath}");
                return configs;
            }

            var libraryDirs = Directory.GetDirectories(rootPath);
            foreach (var dir in libraryDirs)
            {
                var libraryName = Path.GetFileName(dir);
                var config = new LibraryConfig
                {
                    LibraryName = libraryName,
                    AppConfigPath = Path.Combine(dir, "app.config.json"),
                    AppConfigDescriptorsPath = Path.Combine(dir, "app.config.descriptors.json"),
                    AppsettingsPath = Path.Combine(dir, "appsettings.json"),
                    ModelTestConfigPath = Path.Combine(dir, "ModelTestConfig"),
                    StepCatalogPath = Path.Combine(dir, "StepCatalog.json"),
                    ManualTestCatalogPath = Path.Combine(dir, "ManualTestCatalog.json"),
                    DevicesPath = Path.Combine(dir, "devices.json"),
                    InfrastructurePath = Path.Combine(dir, "infrastructure.json"),
                    BarcodeRulesPath = Path.Combine(dir, "barcode_rules.json"),
                    ModelListPath = Path.Combine(dir, "ModelList.json"),
                    DeviceProfilePath = ResolveDeviceProfilePath(dir),
                };

                configs[libraryName] = config;
            }
            return configs;
        }

        /// <summary>
        /// 盐城 legacy 使用 SeatProfile.json；新库使用 DeviceProfile.json。
        /// </summary>
        private static string ResolveDeviceProfilePath(string libraryDir)
        {
            var deviceProfile = Path.Combine(libraryDir, "DeviceProfile.json");
            if (File.Exists(deviceProfile))
            {
                return deviceProfile;
            }

            var seatProfile = Path.Combine(libraryDir, "SeatProfile.json");
            if (File.Exists(seatProfile))
            {
                return seatProfile;
            }

            return deviceProfile;
        }
    }
}
