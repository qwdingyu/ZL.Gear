# 场景 JSON 导读

每个文件都是一份 **DynamicFlow 配方**：Engine 读 JSON → 按顺序执行 → 输出 `OverallSuccess`。

## 建议阅读顺序

| 顺序 | 文件 | 一句话 |
|------|------|--------|
| 1 | `Station_HappyPath.json` | **从这里开始**：行业三步 + Calculate + 双 Assert，最短合格路径 |
| 2 | `Gear_Core_Showcase.json` | 框架能力橱窗（并行/重试/等待/断言），**不含**行业 Handler |
| 3 | `Fork_Seatbelt_Like.json` | 只改 JSON 换行业叙事（安全带 vs 工位 Demo） |
| 4 | `Station_ProbeOverride.json` | 演示 Args 覆盖仿真值（接真表前的契约） |
| 5 | `Station_Integrated_Ate.json` | 行业 + Parallel/WaitUntil 控制流同屏 |

## 门禁专用（故意失败，勿当「产品演示」）

| 文件 | 期望结果 | 证明什么 |
|------|----------|----------|
| `Station_AssertFail.json` | **FAIL** | 不合格时 OverallSuccess 必须为 false |
| `Station_TimeoutContract.json` | **FAIL** | 流程超时不得误 PASS |

## JSON 里看什么？

以 `Station_HappyPath.json` 为例：

1. **顶层** `Command: "DynamicFlow"` — 表示内嵌一整段微流程。  
2. **`WorkflowDefinition.Sequence`** — 步骤列表，从上到下执行。  
3. **`ActionKey: "Industry.Station.ApplyRecipe"`** — 调用你的行业扩展（C# Handler）。  
4. **`ActionKey: "Calculate"` / `"Assert"`** — 框架内置，表达式和判据都在 JSON。  
5. **`Finalizers`** — 无论成败都会执行的清理（类似 `finally`）。

## 运行单个场景

```bash
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- --report run Station_HappyPath
```

`--report` 会在控制台打印步骤树，对照 JSON 逐步理解。
