using System.Collections.Generic;

namespace ZL.Gear.Core.Models
{
    /// <summary>
    /// Ä³¸ö¾ßÌåÐÍºÅµÄ²âÊÔ²½Öè
    /// </summary>
    public class ModelStepConfig
    {
        public string Model { get; set; }
        public List<StepConfig> Steps { get; set; } = new List<StepConfig>();
    }

    public sealed class StepCatalogDto { public List<StepConfig> Steps { get; set; } }
}
