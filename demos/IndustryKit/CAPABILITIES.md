# Gear.NET 能力全景图（IndustryKit 对照）

> **目的**：一眼看清「框架能做什么」与「本 Demo 已证明什么」。  
> **真值**：IndustryKit 跑通的见 `capabilities` 命令；未挂载的见下文「需 Instrumented」。

---

## 一、三层产品面（docs/001）

| 层 | 包/位置 | IndustryKit 体现 |
|----|---------|-------------------|
| **L-DSL** | `ZL.Gear.Core` | Calculate / Assert / 变量 / 表达式 |
| **L-Test** | `ZL.Gear.Engine` | `SequenceExecutor` + `DynamicFlow` + OverallSuccess |
| **L-Adapter** | 你的 Extension | `ZL.Gear.Extension.Station` 三 Handler |

---

## 二、IndustryKit 已演示（LogicOnly + Core）

运行 `dotnet run ... -- capabilities` 查看「能力 ↔ 场景」映射。

| 能力域 | 代表场景 | 命令入口 |
|--------|----------|----------|
| 行业闭环 | `Station_HappyPath.json` | `quickstart` |
| 框架控制流 | `Gear_Core_Showcase.json` | `learn` ② / `showcase` ① |
| Assert L1/L0/L2 | Showcase + `Gear_Assert_L2_Condition.json` | `learn` ③ |
| Args fail-closed | `Station_ProbeOverride.json` | `learn` ④ |
| Parallel + WaitUntil | `Station_Integrated_Ate.json` | `showcase` ③ |
| 节点级 Condition 守卫 + 节点级 Finally | `Gear_Core_Showcase.json` | `learn` ② / `showcase` ① |
| 换行业换 JSON | `Fork_Seatbelt_Like.json` | `showcase` ⑤ |
| 故意 FAIL 门禁 | AssertFail / TimeoutContract | **`verify` only** |
| GlobalContext | 每场景注入 Model/Barcode | `MarkComplete` 日志可见 |

---

## 三、框架具备 · IndustryKit 未挂载（非缺陷）

IndustryKit 使用 `AsLogicOnlyDemoHost()` + `BuiltInModules.Core`，**刻意**不引入仪器副作用。

| 能力 | 去哪看 |
|------|--------|
| Sensing 采样（Continuous/Threshold） | 私有 `ZL.Gear.Demos` · `Demo_Sampling_Continuous.json` |
| GenericMeasure / 真表 Read | Instrumented 宿主 + Mock `IDevice` |
| PLC / AI 模块 | `BuiltInModules.Plc` / `.Ai` · 私有 Drivers |
| 顶层 Verify + ExpectedResults | legacy StepConfig 树 · **ZL.Gear.Demos** `SeatHostBootstrap`（`bootstrap-demo` 仅文档链接） |
| Extensions.Data 落库 | NuGet `ZL.Gear.Extensions.Data` · 单元测试 |

---

## 四、推荐学习路径

```bash
# 10 秒：一条合格工位
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- quickstart

# 2 分钟：6 步深度学习（全 PASS）
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- learn

# 产品演示：5 条橱窗
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- showcase
```

---

## 五、文档索引

| 文档 | 内容 |
|------|------|
| [GETTING_STARTED.md](GETTING_STARTED.md) | 5 分钟上手 |
| [Scenarios/README.md](ZL.Gear.Samples.Industry.Client/Scenarios/README.md) | 逐 JSON 导读 |
| [docs/004](../../docs/004_行业扩展模板与使用场景_2026-09-12.md) | Handler 铁律 |
| [docs/005](../../docs/005_StepArgsReader使用规范_2026-09-12.md) | 参数来源策略 |
| [docs/006](../../docs/006_表达式变量与判定方言_2026-09-12.md) | Assert/Calculate 方言 |
| [docs/007](../../docs/007_验收门禁与测试指南_2026-09-12.md) | verify 7 条门禁 |
| [docs/008](../../docs/008_测试体系审查与优化指南_2026-09-12.md) | 测试金字塔 |
