using System;
using System.Collections.Generic;
using System.Linq;

namespace ZL.Gear.Core.Models
{
    public static class StepExtensions
    {
        /*
         // 检查单个命令
        bool hasSensorCheck = testPrerequisites.Steps.ContainsCommandRecursive("MeasureVoltage");

        // 查找所有包含特定命令的步骤
        var sensorSteps = testPrerequisites.Steps.FindStepsByCommandRecursive("MeasureVoltage", "MeasureCurrent");

        // 获取所有步骤（扁平化列表）
        var allSteps = testPrerequisites.Steps.FlattenSteps();

        // 检查多个不同的命令组合
        bool needsSpecialEquipment = testPrerequisites.Steps.ContainsAnyCommandRecursive(
            "MeasureVoltage", 
            "MeasureCurrent", 
            "HighVoltageTest",
            "PressureTest"
        );
         */
        /// <summary>
        /// 递归检查步骤树中是否包含指定命令的步骤
        /// </summary>
        public static bool ContainsAnyCommandRecursive(this IEnumerable<StepConfig> steps, params string[] commands)
        {
            if (steps == null || !steps.Any() || commands == null || !commands.Any())
                return false;

            var commandSet = new HashSet<string>(commands, StringComparer.OrdinalIgnoreCase);

            foreach (var step in steps)
            {
                if (commandSet.Contains(step.Command))
                    return true;

                if (step.SubSteps != null && step.SubSteps.Any() &&
                    step.SubSteps.ContainsAnyCommandRecursive(commands))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 递归检查步骤树中是否包含指定命令的步骤（单个命令版本）
        /// </summary>
        public static bool ContainsCommandRecursive(this IEnumerable<StepConfig> steps, string command)
        {
            return steps.ContainsAnyCommandRecursive(command);
        }

        /// <summary>
        /// 递归查找所有包含指定命令的步骤
        /// </summary>
        public static List<StepConfig> FindStepsByCommandRecursive(this IEnumerable<StepConfig> steps, params string[] commands)
        {
            var result = new List<StepConfig>();

            if (steps == null || !steps.Any() || commands == null || !commands.Any())
                return result;

            var commandSet = new HashSet<string>(commands, StringComparer.OrdinalIgnoreCase);

            foreach (var step in steps)
            {
                if (commandSet.Contains(step.Command))
                    result.Add(step);

                if (step.SubSteps != null && step.SubSteps.Any())
                {
                    var subResults = step.SubSteps.FindStepsByCommandRecursive(commands);
                    result.AddRange(subResults);
                }
            }

            return result;
        }

        public static IEnumerable<StepConfig> FlattenSteps(StepConfig step)
        {
            yield return step;
            if (step.SubSteps != null)
            {
                foreach (var subStep in step.SubSteps)
                {
                    foreach (var s in FlattenSteps(subStep))
                    {
                        yield return s;
                    }
                }
            }
        }

        /// <summary>
        /// 递归获取步骤树中的所有步骤（扁平化）
        /// </summary>
        public static List<StepConfig> FlattenSteps(this IEnumerable<StepConfig> steps)
        {
            var flattened = new List<StepConfig>();

            if (steps == null) return flattened;

            foreach (var step in steps)
            {
                flattened.Add(step);

                if (step.SubSteps != null && step.SubSteps.Any())
                {
                    flattened.AddRange(step.SubSteps.FlattenSteps());
                }
            }

            return flattened;
        }

        /// <summary>
        /// 递归检查步骤树中是否包含所有指定的命令
        /// </summary>
        public static bool ContainsAllCommandsRecursive(this IEnumerable<StepConfig> steps, params string[] commands)
        {
            if (steps == null || !steps.Any() || commands == null || !commands.Any())
                return false;

            var commandSet = new HashSet<string>(commands, StringComparer.OrdinalIgnoreCase);
            var foundCommands = new HashSet<string>();

            steps.CollectCommandsRecursive(foundCommands);

            return commandSet.IsSubsetOf(foundCommands);
        }

        /// <summary>
        /// 递归收集所有步骤的命令
        /// </summary>
        private static void CollectCommandsRecursive(this IEnumerable<StepConfig> steps, HashSet<string> commandSet)
        {
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (!string.IsNullOrEmpty(step.Command))
                    commandSet.Add(step.Command);

                if (step.SubSteps != null && step.SubSteps.Any())
                    step.SubSteps.CollectCommandsRecursive(commandSet);
            }
        }
    }

}
