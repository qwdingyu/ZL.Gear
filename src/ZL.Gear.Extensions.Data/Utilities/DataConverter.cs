using ZL.Gear.Extensions.Data.Abstractions;
using ZL.Gear.Extensions.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using CoreRunner = ZL.Gear.Core.Runner;
using CoreStepHandler = ZL.Gear.Core.StepHandler;
using CoreModels = ZL.Gear.Core.Models;

namespace ZL.Gear.Extensions.Data.Utilities
{
    /// <summary>
    /// 数据转换工具 - Entity 与 Model 之间的映射
    /// </summary>
    public static class DataConverter
    {
        /// <summary>
        /// 将 Core 的 TestRunResult 转换为存储模型
        /// </summary>
        public static TestResultModel ToStorageModel(CoreRunner.TestRunResult runResult, string barcode, string model, string stationNo)
        {
            var modelObj = new TestResultModel
            {
                Barcode = barcode,
                Model = model,
                StationNo = stationNo,
                TestStartTime = runResult.StartTime,
                FinalResult = runResult.OverallSuccess ? "PASS" : "FAIL",
                TotalDurationSec = double.TryParse(runResult.TotalDurationSeconds, out var dur) ? dur : 0,
                IsTransmitted = false,
                Items = new List<TestItemModel>()
            };

            if (runResult.StepResults != null)
            {
                foreach (var step in FlattenSteps(runResult.StepResults))
                {
                    var measurements = step.StepMeasurements ?? new List<CoreModels.Measurement>();
                    
                    if (measurements.Any())
                    {
                        foreach (var measure in measurements)
                        {
                            var item = new TestItemModel
                            {
                                StepKey = step.StepKey,
                                TestItem = measure.Key,
                                TestValue = measure.Value?.ToString(),
                                TestResult = step.Outcome == CoreModels.StepOutcome.Passed ? "PASS" : "FAIL",
                                StepStartTime = step.StartTime,
                                Unit = measure.Unit,
                                LCL = null,
                                UCL = null
                            };

                            if (double.TryParse(item.TestValue, out double val))
                            {
                                item.MetricValue = val;
                            }

                            modelObj.Items.Add(item);
                        }
                    }
                    else
                    {
                        var item = new TestItemModel
                        {
                            StepKey = step.StepKey,
                            TestItem = "",
                            TestValue = "",
                            TestResult = step.Outcome == CoreModels.StepOutcome.Passed ? "PASS" : "FAIL",
                            StepStartTime = step.StartTime,
                            Unit = null,
                            LCL = null,
                            UCL = null
                        };
                        modelObj.Items.Add(item);
                    }
                }
            }

            return modelObj;
        }

        /// <summary>
        /// 将实体转换为存储模型
        /// </summary>
        public static TestResultModel ToModel(this TestResultEntity entity)
        {
            return new TestResultModel
            {
                Id = entity.Id,
                Barcode = entity.Barcode,
                Model = entity.Model,
                StationNo = entity.StationNo,
                TestStartTime = entity.TestStartTime,
                FinalResult = entity.FinalResult,
                TotalDurationSec = entity.TotalDurationSec,
                IsTransmitted = entity.IsTransmitted,
                TransmitTime = entity.TransmitTime,
                TransmitMessage = entity.TransmitMessage,
                Items = entity.Items?.Select(i => i.ToModel()).ToList() ?? new List<TestItemModel>()
            };
        }

        /// <summary>
        /// 将实体项转换为存储模型
        /// </summary>
        public static TestItemModel ToModel(this ResultItemEntity entity)
        {
            return new TestItemModel
            {
                Id = entity.Id,
                TestResultsId = entity.TestResultsId,
                StepKey = entity.StepKey,
                TestItem = entity.TestItem,
                TestValue = entity.TestValue,
                Unit = entity.Unit,
                LCL = entity.LCL,
                UCL = entity.UCL,
                MetricValue = entity.MetricValue,
                TestResult = entity.TestResult,
                StepStartTime = entity.StepStartTime
            };
        }

        /// <summary>
        /// 将存储模型转换为主表实体
        /// </summary>
        public static TestResultEntity ToMasterEntity(this TestResultModel model)
        {
            return new TestResultEntity
            {
                Barcode = model.Barcode,
                Model = model.Model,
                StationNo = model.StationNo,
                TestStartTime = model.TestStartTime,
                FinalResult = model.FinalResult,
                TotalDurationSec = model.TotalDurationSec,
                IsTransmitted = model.IsTransmitted,
                TransmitTime = model.TransmitTime,
                TransmitMessage = model.TransmitMessage
            };
        }

        /// <summary>
        /// 将存储模型转换为子表实体
        /// </summary>
        public static ResultItemEntity ToDetailEntity(this TestItemModel model, long masterId)
        {
            return new ResultItemEntity
            {
                TestResultsId = masterId,
                StepKey = model.StepKey,
                TestItem = model.TestItem,
                TestValue = model.TestValue,
                Unit = model.Unit,
                LCL = model.LCL,
                UCL = model.UCL,
                MetricValue = model.MetricValue,
                TestResult = model.TestResult,
                StepStartTime = model.StepStartTime
            };
        }

        /// <summary>
        /// 递归扁平化步骤结果
        /// </summary>
        private static IEnumerable<CoreStepHandler.StepRunResult> FlattenSteps(IEnumerable<CoreStepHandler.StepRunResult> steps)
        {
            var result = new List<CoreStepHandler.StepRunResult>();

            foreach (var step in steps ?? Enumerable.Empty<CoreStepHandler.StepRunResult>())
            {
                result.Add(step);

                if (step.SubStepResults != null && step.SubStepResults.Any())
                {
                    result.AddRange(FlattenSteps(step.SubStepResults));
                }
            }

            return result;
        }
    }
}
