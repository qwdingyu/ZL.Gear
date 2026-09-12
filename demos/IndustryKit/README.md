# IndustryKit — 搞懂 ZL.Gear 怎么用的 Demo

> **给谁看**：第一次接触 ZL.Gear 的客户开发者、集成工程师。  
> **证明什么**：JSON 写测试序列 + 行业 Handler 插件 + 执行与 Assert 分离 —— **无需仪器**即可跑通。  
> **详细上手**：[`GETTING_STARTED.md`](GETTING_STARTED.md)（5 分钟 · 复制粘贴即可）

---

## 30 秒：这个 Demo 是什么？

ZL.Gear 把产线测试写成 **JSON 配方**，Engine 负责执行。  
IndustryKit 用**模拟电阻工位**演示完整链路：

```text
Station_HappyPath.json
  → ApplyRecipe（写限值 5Ω、仿真 2.5Ω）
  → ProbeChannel（模拟仪表读数）
  → Calculate（Margin = Limit - Measured）
  → Assert（判合格）
  → MarkComplete
  → OverallSuccess
```

**你要复制的**：`ZL.Gear.Extension.Station`（行业插件）+ `Program.cs` 里宿主组装 + `Scenarios/*.json`（配方）。  
**你要引用的 NuGet**：`ZL.Gear.Core` + `ZL.Gear.Engine`。

---

## 5 分钟上手

在仓库根目录：

```bash
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- quickstart
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- learn
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- capabilities
```

**全系统能力地图**：[`CAPABILITIES.md`](CAPABILITIES.md)（LogicOnly 已演示 vs Instrumented/私有轨）。

无参数 `dotnet run ...` 只打印欢迎说明，**不会**跑 7 条 CI 门禁。

单场景 + 步骤树：

```bash
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- --report run Station_HappyPath
```

---

## 目录结构（打开仓库先看哪）

| 路径 | 你是谁 | 先看什么 |
|------|--------|----------|
| [`GETTING_STARTED.md`](GETTING_STARTED.md) | 客户开发者 | **全文** |
| [`CAPABILITIES.md`](CAPABILITIES.md) | 所有人 | 能力全景图 |
| `Scenarios/Station_HappyPath.json` | 写配方的人 | 最短合格路径 JSON |
| `Scenarios/README.md` | 写配方的人 | 8 个场景逐文件说明 |
| `ZL.Gear.Extension.Station/Handlers/` | 写行业代码的人 | 三个 Handler 样板 |
| `Program.cs` | 集成宿主的人 | `SequenceExecutorBuilder` 组装 |
| `VerificationCatalog.cs` | 维护者 / CI | 发版门禁 7 条（含故意 FAIL） |

```text
demos/IndustryKit/
├── GETTING_STARTED.md          ← 客户开发者入口
├── README.md                   ← 本文件
├── verify.sh                   ← CI 门禁包装（维护者）
├── ZL.Gear.Extension.Station/  ← 复制到你的项目
└── ZL.Gear.Samples.Industry.Client/
    ├── Program.cs              ← 宿主样板
    ├── OnboardingGuide.cs      ← quickstart 引导文案
    ├── ShowcaseCatalog.cs      ← showcase 顺序
    ├── CapabilityCatalog.cs    ← 能力矩阵（已演示）
    ├── SystemCapabilityMap.cs  ← Instrumented/私有能力清单
    ├── LearningCatalog.cs      ← learn 命令 6 步路径
    └── Scenarios/              ← JSON 配方库（8 个）
```

---

## 换行业 / 换产品要改什么？

| 需求 | 改什么 | 不改什么 |
|------|--------|----------|
| 换限值、换产品型号 | 复制 JSON，改 `RecipeId` / `LimitOhm` / Assert | Extension DLL |
| 换测试项（新动作） | 复制 Extension，加 Handler + `RegisterHandlerWithAction` | Engine |
| 接真实仪表 | 改 `ProbeChannelHandler` 读设备，**保持** `MeasuredOhm` 变量名 | JSON Assert 结构 |
| 嵌进 WinForms/服务 | 抄 `Program.cs` 里 Build 段到你的启动代码 | IndustryKit Client 整包 |

---

## Handler 铁律（复制 Extension 时必守）

| 规则 | 做法 | 反例（产线灾难） |
|------|------|------------------|
| 限值 fail-closed | `ArgsOnly` + `TryRequire*` | `context.Get` / `All` 静默兜底 |
| 写流程变量 | `args.SetShared` | Handler 内 `Variables.Set` |
| 读前序状态 | `GetFlowString` / `VariablesOnly` | `All` 误读 Global |
| 合格判定 | JSON `Assert` | Handler 内 `if (x > limit)` |

---

## showcase vs verify（别搞混）

| | showcase | verify |
|---|----------|--------|
| **给谁** | 客户开发者、产品演示 | CI / 发版维护者 |
| **条数** | 5 条，**全部期望 PASS** | 7 条，含 **2 条必须 FAIL** |
| **命令** | `showcase` | `verify` 或 `verify.sh` |
| **能否替代** | — | showcase **不能**替代 verify |

---

## CI / 维护者

```bash
./demos/IndustryKit/verify.sh   # 须输出 INDUSTRY_KIT_VERIFY_PASS
```

真值源：`VerificationCatalog.cs`。规范见 [docs/004](../../docs/004_行业扩展模板与使用场景_2026-09-12.md) · [docs/007](../../docs/007_验收门禁与测试指南_2026-09-12.md)。

---

## 与 ConsoleApp 关系

- 官方全栈场景库在私有仓 `ZL.Gear.Demos`。  
- IndustryKit 是 **MIT 公开、可复制的第二宿主**，专门降低「第一次集成 ZL.Gear」的认知门槛。
