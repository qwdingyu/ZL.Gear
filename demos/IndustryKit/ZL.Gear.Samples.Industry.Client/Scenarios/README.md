# 场景 JSON 导读

每个文件都是一份 **DynamicFlow 配方**：Engine 读 JSON → 按顺序执行 → 输出 `OverallSuccess`。

> **快速入口**：`quickstart`（1 条）→ `learn`（6 条）→ `showcase`（5 条）→ `verify`（7 条门禁）

---

## 建议学习顺序（`learn` 命令）

| 步 | 文件 | 一句话 |
|----|------|--------|
| 1 | `Station_HappyPath.json` | **从这里开始**：行业三步 + Calculate + 双 Assert |
| 2 | `Gear_Core_Showcase.json` | 框架能力（并行/重试/等待），**不含**行业 Handler |
| 3 | `Gear_Assert_L2_Condition.json` | Assert 第三种方言：`Condition` 布尔/表达式 |
| 4 | `Station_ProbeOverride.json` | Args 覆盖仿真值（接真表前的契约） |
| 5 | `Station_Integrated_Ate.json` | 行业 + Parallel/WaitUntil 控制流 |
| 6 | `Fork_Seatbelt_Like.json` | 只改 JSON 换行业叙事 |

---

## showcase 橱窗（5 条 · 全 PASS）

| 顺序 | 文件 | 亮点 |
|------|------|------|
| ① | `Gear_Core_Showcase.json` | 框架内核 |
| ② | `Station_HappyPath.json` | 最短行业闭环 |
| ③ | `Station_Integrated_Ate.json` | 一体化 ATE |
| ④ | `Station_ProbeOverride.json` | Args fail-closed |
| ⑤ | `Fork_Seatbelt_Like.json` | JSON 分叉 |

---

## verify 门禁（7 条 · 含 2 条必须 FAIL）

| ID | 文件 | 期望 | 证明什么 |
|----|------|------|----------|
| — | `Gear_Core_Showcase.json` | PASS | 框架内核 |
| — | `Station_HappyPath.json` | PASS | 行业闭环 |
| — | `Station_Integrated_Ate.json` | PASS | 控制流+行业 |
| — | `Station_ProbeOverride.json` | PASS | Args 覆盖 |
| — | `Fork_Seatbelt_Like.json` | PASS | JSON 分叉 |
| — | `Station_AssertFail.json` | **FAIL** | 不合格不得假绿 |
| — | `Station_TimeoutContract.json` | **FAIL** | 流程超时契约 |

真值源：`VerificationCatalog.cs`（与上表语义一致，含 Summary 子串校验）。

---

## JSON 里看什么？

以 `Station_HappyPath.json` 为例：

1. **顶层** `Command: "DynamicFlow"` — 内嵌一整段微流程。  
2. **`WorkflowDefinition.Sequence`** — 步骤列表，从上到下执行。  
3. **`ActionKey: "Industry.Station.ApplyRecipe"`** — 行业扩展（C# Handler）。  
4. **`ActionKey: "Calculate"` / `"Assert"`** — 框架内置；判据在 JSON。  
5. **`Finalizers`** — 无论成败都执行（类似 `finally`）。  
6. **`EvaluateResult: false`** — 跳过步骤级 ExpectedResults；**DynamicFlow 内 Assert 仍决定 OverallSuccess**。

---

## 运行单个场景

```bash
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- --report run Station_HappyPath
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- --report run Gear_Assert_L2_Condition
```

`--report` 打印步骤树，对照 JSON 逐步理解。
