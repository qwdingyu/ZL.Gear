using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZL.Gear.Core.Models
{
    /// <summary>
    /// 步骤工具类，承载所有关于 StepConfig 的计算、转换和更新逻辑。
    /// </summary>
    public static class StepKit
    {
        public static int CountAllSteps(IEnumerable<StepConfig> steps)
        {
            if (steps == null) return 0;
            return steps.Sum(CountStepsRecursive);
        }

        public static int CountStepsRecursive(StepConfig config)
        {
            if (config == null) return 0;
            int count = 1;
            if (config.SubSteps != null)
            {
                count += config.SubSteps.Sum(CountStepsRecursive);
            }
            return count;
        }

        public static List<StepConfig> RemoveNoiseSteps(IEnumerable<StepConfig> steps)
        {
            if (steps == null) return new List<StepConfig>();
            var result = new List<StepConfig>();

            foreach (var step in steps)
            {
                if (step.Command != null && step.Command.Contains("Noise")) continue;

                var clonedStep = (StepConfig)step.Clone();
                if (clonedStep.SubSteps != null && clonedStep.SubSteps.Any())
                {
                    clonedStep.SubSteps = RemoveNoiseSteps(clonedStep.SubSteps);
                }
                result.Add(clonedStep);
            }

            return result;
        }

        public static IEnumerable<StepConfig> FlattenSteps(this StepConfig step)
        {
            if (step == null) yield break;
            yield return step;
            if (step.SubSteps != null)
            {
                foreach (var sub in step.SubSteps)
                {
                    foreach (var f in FlattenSteps(sub)) yield return f;
                }
            }
        }

        public static List<StepConfig> FlattenAll(this IEnumerable<StepConfig> steps)
        {
            var result = new List<StepConfig>();
            if (steps == null) return result;
            foreach (var step in steps)
            {
                result.AddRange(FlattenSteps(step));
            }
            return result;
        }

        /// <summary>
        /// 更新目标配置，支持递归子步骤更新。
        /// </summary>
        public static void UpdateFrom(this StepConfig target, StepConfig source, StepConfigUpdateOptions options = null)
        {
            if (target == null || source == null) return;

            options = options ?? new StepConfigUpdateOptions();
            var skipSet = new HashSet<string>(options.SkipProperties ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            // 更新基础属性 (反射或硬编码)
            UpdateBasicProperties(target, source, skipSet);

            // 更新引用集合
            UpdateReferenceCollections(target, source, options, skipSet);

            // 递归子步骤
            if (options.UpdateSubSteps)
            {
                UpdateSubStepsRecursively(target, source, options);
            }
        }

        private static void UpdateBasicProperties(StepConfig target, StepConfig source, HashSet<string> skipSet)
        {
            if (!skipSet.Contains(nameof(StepConfig.StepKey))) target.StepKey = source.StepKey;
            if (!skipSet.Contains(nameof(StepConfig.StepName))) target.StepName = source.StepName;
            if (!skipSet.Contains(nameof(StepConfig.Description))) target.Description = source.Description;
            if (!skipSet.Contains(nameof(StepConfig.ExecutionMode))) target.ExecutionMode = source.ExecutionMode;
            if (!skipSet.Contains(nameof(StepConfig.ExecutionType))) target.ExecutionType = source.ExecutionType;
            if (!skipSet.Contains(nameof(StepConfig.StepType))) target.StepType = source.StepType;
            if (!skipSet.Contains(nameof(StepConfig.Target))) target.Target = source.Target;
            if (!skipSet.Contains(nameof(StepConfig.Command))) target.Command = source.Command;
            if (!skipSet.Contains(nameof(StepConfig.PromptString1))) target.PromptString1 = source.PromptString1;
            if (!skipSet.Contains(nameof(StepConfig.PromptString2))) target.PromptString2 = source.PromptString2;
            if (!skipSet.Contains(nameof(StepConfig.PicturePath))) target.PicturePath = source.PicturePath;
            if (!skipSet.Contains(nameof(StepConfig.TimeoutMs))) target.TimeoutMs = source.TimeoutMs;
            if (!skipSet.Contains(nameof(StepConfig.Enable))) target.Enable = source.Enable;
            if (!skipSet.Contains(nameof(StepConfig.StopByFail))) target.StopByFail = source.StopByFail;
            if (!skipSet.Contains(nameof(StepConfig.StartDelayMs))) target.StartDelayMs = source.StartDelayMs;
            if (!skipSet.Contains(nameof(StepConfig.CanSingleTest))) target.CanSingleTest = source.CanSingleTest;
        }

        private static void UpdateReferenceCollections(StepConfig target, StepConfig source, StepConfigUpdateOptions options, HashSet<string> skipSet)
        {
            if (!skipSet.Contains(nameof(StepConfig.AdditionalTargets)))
            {
                target.AdditionalTargets = source.AdditionalTargets != null ? new List<string>(source.AdditionalTargets) : new List<string>();
            }

            if (options.UpdateParameters && !skipSet.Contains(nameof(StepConfig.Parameters)))
            {
                target.Parameters = source.Parameters != null ? new Dictionary<string, object>(source.Parameters) : new Dictionary<string, object>();
            }

            if (options.UpdateExpectedResults && !skipSet.Contains(nameof(StepConfig.ExpectedResults)))
            {
                target.ExpectedResults = source.ExpectedResults != null
                    ? source.ExpectedResults.Select(spec => (ExpectedSpec)spec.Clone()).ToList()
                    : new List<ExpectedSpec>();
            }
        }

        private static void UpdateSubStepsRecursively(StepConfig target, StepConfig source, StepConfigUpdateOptions options)
        {
            if (source.SubSteps == null) { target.SubSteps = new List<StepConfig>(); return; }

            switch (options.SubStepsUpdateStrategy)
            {
                case SubStepsUpdateStrategy.Replace:
                    target.SubSteps = source.SubSteps.Where(s => s != null).Select(s => (StepConfig)s.Clone()).ToList();
                    break;
                case SubStepsUpdateStrategy.MergeByStepKey:
                    MergeSubStepsByStepKey(target, source.SubSteps, options);
                    break;
                case SubStepsUpdateStrategy.MergeByIndex:
                    MergeSubStepsByIndex(target, source.SubSteps, options);
                    break;
            }
        }

        private static void MergeSubStepsByStepKey(StepConfig target, List<StepConfig> sourceSubSteps, StepConfigUpdateOptions options)
        {
            var targetSubSteps = target.SubSteps ?? new List<StepConfig>();
            var targetDict = targetSubSteps.ToDictionary(s => s.StepKey, s => s);
            var result = new List<StepConfig>();

            foreach (var sourceStep in sourceSubSteps)
            {
                if (targetDict.TryGetValue(sourceStep.StepKey, out var targetStep))
                {
                    UpdateFrom(targetStep, sourceStep, options.CreateChildOptions());
                    result.Add(targetStep);
                }
                else if (options.AddNewSubSteps)
                {
                    result.Add((StepConfig)sourceStep.Clone());
                }
            }

            if (options.KeepExtraTargetSubSteps)
            {
                var sourceKeys = new HashSet<string>(sourceSubSteps.Select(s => s.StepKey));
                foreach (var tStep in targetSubSteps)
                {
                    if (!sourceKeys.Contains(tStep.StepKey)) result.Add(tStep);
                }
            }

            target.SubSteps = result;
        }

        private static void MergeSubStepsByIndex(StepConfig target, List<StepConfig> sourceSubSteps, StepConfigUpdateOptions options)
        {
            var targetSubSteps = target.SubSteps ?? new List<StepConfig>();
            var result = new List<StepConfig>();
            int maxLength = Math.Max(sourceSubSteps.Count, targetSubSteps.Count);

            for (int i = 0; i < maxLength; i++)
            {
                if (i < sourceSubSteps.Count && i < targetSubSteps.Count)
                {
                    var tStep = targetSubSteps[i];
                    UpdateFrom(tStep, sourceSubSteps[i], options.CreateChildOptions());
                    result.Add(tStep);
                }
                else if (i < sourceSubSteps.Count && options.AddNewSubSteps)
                {
                    result.Add((StepConfig)(sourceSubSteps[i]).Clone());
                }
                else if (i < targetSubSteps.Count && options.KeepExtraTargetSubSteps)
                {
                    result.Add(targetSubSteps[i]);
                }
            }
            target.SubSteps = result;
        }

        public static Dictionary<string, object> ParseSensorSpecs(StepConfig step)
        {
            var specs = new Dictionary<string, object>();
            if (step.Parameters != null && step.Parameters.TryGetValue("CustomerParam", out var cp) && cp != null)
            {
                string customerParam = cp.ToString();
                if (!string.IsNullOrWhiteSpace(customerParam))
                {
                    var specNames = new[] { "Analog1", "Analog2", "Analog3" };
                    string[] specsStr = customerParam.Split(',');
                    int itemsToProcess = Math.Min(specNames.Length, specsStr.Length);
        
                    for (int i = 0; i < itemsToProcess; i++)
                    {
                        string name = specNames[i];
                        string[] lclucl = specsStr[i].Split('|');
                        if (lclucl.Length == 2 && int.TryParse(lclucl[0], out int lcl) && int.TryParse(lclucl[1], out int ucl))
                        {
                            // 使用 Dictionary 替代 SensorSpec 对象
                            specs[name] = new Dictionary<string, object>
                            {
                                ["Name"] = name,
                                ["LCL"] = lcl,
                                ["UCL"] = ucl
                            };
                        }
                    }
                }
            }
            return specs;
        }
    }
}
