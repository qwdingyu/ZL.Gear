using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZL.Gear.Core.Configuration;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Core
{
    public static class StepCatalogService
    {
        private static string _catalogPath;
        private static string _manualTestCatalogPath;
        private static LibraryConfig libraryConfig = ProjectLibraryManager.GetCurrentLibraryConfig();
        static StepCatalogService()
        {
            _catalogPath = libraryConfig.StepCatalogPath;
            _manualTestCatalogPath = libraryConfig.ManualTestCatalogPath;
        }
        public static StepCatalogDto loadAllStepCatalog(bool showAll = false)
        {
            var catalog = LoadCatalogInternal(_catalogPath);
            if (!showAll && catalog.Steps != null)
            {
                catalog.Steps = catalog.Steps.Where(s => s.Enable).ToList();
            }
            return catalog;
        }
        public static StepCatalogDto loadManualTestCatalog(bool showAll = false)
        {
            var catalog = LoadCatalogInternal(_manualTestCatalogPath);
            if (!showAll && catalog.Steps != null)
            {
                catalog.Steps = catalog.Steps.Where(s => s.Enable).ToList();
            }
            return catalog;
        }

        private static StepCatalogDto LoadCatalogInternal(string path)
        {
            var result = new StepCatalogDto { Steps = new List<StepConfig>() };
            if (string.IsNullOrEmpty(path)) return result;

            try
            {
                if (Directory.Exists(path))
                {
                    // 递归加载目录下所有 .json 文件
                    var files = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var content = File.ReadAllText(file);
                        // 支持两种格式：直接是 StepConfig 数组，或者包装在 StepCatalogDto 中
                        if (content.TrimStart().StartsWith("["))
                        {
                            var steps = JsonConvert.DeserializeObject<List<StepConfig>>(content);
                            if (steps != null) result.Steps.AddRange(steps);
                        }
                        else
                        {
                            var dto = JsonConvert.DeserializeObject<StepCatalogDto>(content);
                            if (dto?.Steps != null) result.Steps.AddRange(dto.Steps);
                        }
                    }
                }
                else if (File.Exists(path))
                {
                    var content = File.ReadAllText(path);
                    var dto = JsonConvert.DeserializeObject<StepCatalogDto>(content);
                    if (dto?.Steps != null) result.Steps = dto.Steps;
                }
            }
            catch (Exception ex)
            {
                LogKit.Error($"加载步骤目录失败 ({path}): {ex.Message}");
            }

            return result;
        }
        public static void SaveToFile(StepCatalogDto stepCatalog, string filePath = "")
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) { filePath = _catalogPath; }
                string json = JsonConvert.SerializeObject(stepCatalog, Formatting.Indented);

                // 确保目录存在
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, json);
                LogKit.Info($"总步骤配置已保存到: {filePath}");
            }
            catch (Exception ex)
            {
                LogKit.Error($"保存总步骤配置失败: {ex.Message}");
                throw;
            }
        }
    }
}
