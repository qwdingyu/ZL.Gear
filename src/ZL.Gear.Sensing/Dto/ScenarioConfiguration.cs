using System;
using System.Collections.Generic;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing.Abstractions;

namespace ZL.Gear.Sensing.Dto
{

    /// <summary>
    /// 编排器执行一个场景所需要的所有信息。
    /// </summary>
    public class ScenarioConfiguration
    {
        public IMeasurable Measurable { get; set; }
        public ScenarioType Type { get; set; }
        public StepContext Context { get; set; }
        public Dictionary<string, object> Args { get; set; }
        public Action<string> Log { get; set; }
    }
}
