using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZL.Gear.Core.Configuration;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Services;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Core
{
    public class StepCatalogService : IStepCatalogService
    {
        private readonly ILibraryService _libraryService;

        public StepCatalogService(ILibraryService libraryService)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        }

        public StepCatalogDto LoadAllStepCatalog(bool showAll = false)
        {
            var path = _libraryService.CurrentLibraryConfig.StepCatalogPath;
            var catalog = LoadCatalogInternal(path);
            
            if (!showAll && catalog.Steps != null)
            {
                catalog.Steps = catalog.Steps.Where(s => s.Enable).ToList();
            }
            return catalog;
        }

        public StepCatalogDto LoadManualTestCatalog(bool showAll = false)
        {
            var path = _libraryService.CurrentLibraryConfig.ManualTestCatalogPath;
            var catalog = LoadCatalogInternal(path);
            
            if (!showAll && catalog.Steps != null)
            {
                catalog.Steps = catalog.Steps.Where(s => s.Enable).ToList();
            }
            return catalog;
        }

        private StepCatalogDto LoadCatalogInternal(string path)
        {
            var result = new StepCatalogDto { Steps = new List<StepConfig>() };
            if (string.IsNullOrEmpty(path)) return result;

            try
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var content = File.ReadAllText(file);
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

        public void SaveToFile(StepCatalogDto stepCatalog, string filePath = "")
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) 
                { 
                    filePath = _libraryService.CurrentLibraryConfig.StepCatalogPath; 
                }
                
                string json = JsonConvert.SerializeObject(stepCatalog, Formatting.Indented);
                var directory = Path.GetDirectoryName(filePath);
                
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, json);
                LogKit.Info($"步骤配置已保存到: {filePath}");
            }
            catch (Exception ex)
            {
                LogKit.Error($"保存步骤配置失败: {ex.Message}");
                throw;
            }
        }
    }
}
