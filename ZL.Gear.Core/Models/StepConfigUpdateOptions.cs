namespace ZL.Gear.Core.Models
{

    /// <summary>
    /// 更新选项配置
    /// </summary>
    public class StepConfigUpdateOptions
    {
        /// <summary>
        /// 是否更新Parameters字典
        /// </summary>
        public bool UpdateParameters { get; set; } = true;

        /// <summary>
        /// 是否递归更新子步骤
        /// </summary>
        public bool UpdateSubSteps { get; set; } = true;

        /// <summary>
        /// 是否更新期望结果
        /// </summary>
        public bool UpdateExpectedResults { get; set; } = true;

        /// <summary>
        /// 子步骤更新策略
        /// </summary>
        public SubStepsUpdateStrategy SubStepsUpdateStrategy { get; set; } = SubStepsUpdateStrategy.MergeByStepKey;

        /// <summary>
        /// 是否添加源中存在但目标中不存在的新子步骤
        /// </summary>
        public bool AddNewSubSteps { get; set; } = true;

        /// <summary>
        /// 是否保留目标中存在但源中不存在的子步骤
        /// </summary>
        public bool KeepExtraTargetSubSteps { get; set; } = true;

        /// <summary>
        /// 需要跳过的属性名列表
        /// </summary>
        public string[] SkipProperties { get; set; }

        /// <summary>
        /// 创建子步骤更新选项（可以调整递归更新的行为）
        /// </summary>
        public StepConfigUpdateOptions CreateChildOptions()
        {
            // 对于子步骤，通常我们保持相同的更新参数，但可以根据需要调整
            return new StepConfigUpdateOptions
            {
                UpdateParameters = this.UpdateParameters,
                UpdateSubSteps = true, // 继续递归更新
                UpdateExpectedResults = this.UpdateExpectedResults,
                SubStepsUpdateStrategy = this.SubStepsUpdateStrategy,
                AddNewSubSteps = this.AddNewSubSteps,
                KeepExtraTargetSubSteps = this.KeepExtraTargetSubSteps,
                SkipProperties = this.SkipProperties
            };
        }
    }

    /// <summary>
    /// 子步骤更新策略
    /// </summary>
    public enum SubStepsUpdateStrategy
    {
        /// <summary>
        /// 完全替换子步骤列表
        /// </summary>
        Replace,

        /// <summary>
        /// 根据StepKey合并更新
        /// </summary>
        MergeByStepKey,

        /// <summary>
        /// 根据索引位置合并更新
        /// </summary>
        MergeByIndex
    }
}

