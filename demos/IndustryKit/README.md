# IndustryKit — 最新架构行业可扩展 Demo

> 取代「早期 Seat 私有 DLL + 代码拼步骤」示范路径。  
> 对齐：[docs/138](../../docs/138_ADR_微流程与执行器抽取边界_2026-09-10.md) · [docs/134–137](../../docs/) · [docs/139](../../docs/139_IndustryKit_行业扩展模板与客户端闭环_2026-09-10.md)

## 前置说明（设计裁决）

| 学什么 | 不学什么（Seat 遗产） |
|--------|----------------------|
| `IGearExtension` + `RegisterHandlerWithAction` | 绑 `libs/PFLite|PlcBase` |
| DynamicFlow JSON + docs/133 新方言 | SeatDemo 代码拼 `StepConfig` 树 |
| `SequenceExecutorBuilder.WithExtension` + `BuiltInModules.Core` | 假定 Seat 已在 ModuleLoader 默认列表 |
| 验证清单含 **故意 FAIL / 超时** | 只跑 HappyPath 假绿 |

三层产品面：

```text
Client（本目录）
  └─ L-Test  SequenceExecutor
        ├─ L-DSL   DynamicFlow（Scenarios/*.json）
        └─ L-Adapter  StationExtension（可换成 Battery/PCBA…）
```

## 目录

```text
demos/IndustryKit/
├── README.md
├── verify.sh
├── ZL.Gear.Extension.Station/     # 薄扩展，仅引用 Core
└── ZL.Gear.Samples.Industry.Client/
    ├── Program.cs                 # run / verify / list
    ├── VerificationCatalog.cs     # 期望结果闭环
    └── Scenarios/
        ├── Station_HappyPath.json
        ├── Station_AssertFail.json      # 期望失败
        ├── Station_TimeoutContract.json # 期望失败
        └── Fork_Seatbelt_Like.json      # 仅 JSON 换行业叙事
```

## 客户端用法

在仓库根目录：

```bash
# 闭环验证（须打印 INDUSTRY_KIT_VERIFY_PASS，exit 0）
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- verify

# 或
./demos/IndustryKit/verify.sh

# 单场景
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- run Station_HappyPath
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- list
```

## 如何扩展到其他行业（惯例）

1. **先配方、后代码**：复制 `Fork_Seatbelt_Like.json`，改 `RecipeId` / 限值 / Assert 文案。能跑通则不要新建 Handler。  
2. **真有行业特异动作**：复制 `ZL.Gear.Extension.Station` → `ZL.Gear.Extension.Battery`（或 PCBA），改命令前缀 `Industry.Battery.*`，在 `Initialize` 注册。  
3. **设备接入**：把 `ProbeChannelHandler` 换成租真实仪表 + `Read`/`Query`，**共享变量键名保持不变**，JSON 可不动。  
4. **宿主**：`WithBuiltInModules(Core | Sensing | Plc)` 按需勾选；`WithExtension(new XxxExtension())`。  
5. **禁止**：把行业逻辑塞进 `ZL.Gear.Engine`；禁止抽「通用 Workflow NuGet」（见 ADR 138）。

## 闭环证明什么

| 用例 | 期望 | 证明 |
|------|------|------|
| HappyPath | Success | 扩展 ActionKey + Calculate + Assert L1 |
| AssertFail | !Success | 不合格不得误 PASS |
| TimeoutContract | !Success | 流程超时契约 |
| Fork_Seatbelt | Success | 换行业可先只改 JSON |

## 与 ConsoleApp 关系

- 官方场景库仍在 `demos/ZL.Gear.ConsoleApp/Scenarios/`（含 `Industry_PCBA_*` 等）。  
- 本 Kit 是 **可复制的第二宿主模板**（扩展缝 + 客户端验证），不替代主场景库。
