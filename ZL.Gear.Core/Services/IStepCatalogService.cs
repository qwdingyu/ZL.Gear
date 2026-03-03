using System.Collections.Generic;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Services
{
    public interface IStepCatalogService
    {
        StepCatalogDto LoadAllStepCatalog(bool showAll = false);
        StepCatalogDto LoadManualTestCatalog(bool showAll = false);
        void SaveToFile(StepCatalogDto stepCatalog, string filePath = "");
    }
}
