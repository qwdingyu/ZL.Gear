namespace ZL.Gear.Core.Workflow
{
    /// <summary>
    /// 工作流节点类型
    /// </summary>
    public enum WorkflowNodeType
    {
        Action,             // 普通原子动作 (Resolve from Registry)
        Measure,            // 测量动作 (带返回值)
        Parallel,           // 并行执行容器
        ParallelMeasure,    // 并行测量容器 (汇总结果)
        Delay,              // 延时等待
        Retry,              // 异常重试包装
        Sequence,           // 顺序执行序列 (容器)
        WaitUntil,          // 条件轮询等待
        Group               // DSL 视觉/逻辑分组别名；运行时与 Sequence 同（扁平展开 Children）
    }
}
