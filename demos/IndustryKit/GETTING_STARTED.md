# IndustryKit — 5 分钟上手（给客户开发者）

> 你**不需要**先读完全部 docs，也**不需要**仪器驱动。本页只回答三件事：**Gear 是什么、Demo 证明什么、你怎么抄到自己的项目**。

---

## 1. 用一句话理解 ZL.Gear

**把产线测试步骤写成 JSON，引擎负责执行；设备动作在 Handler，合格判据在 JSON 的 Assert。**

类比：OpenTAP / TestStand 的「Plan + Step 插件」，但我们是 **Headless .NET 库 + JSON 配方**，适合嵌进你的 WinForms/WPF/服务里，而不是再买一个重型 IDE。

---

## 2. 这个 Demo 在干什么？

IndustryKit 模拟一个**电阻测试工位**（无真实硬件）：

```text
JSON 配方（Scenarios/*.json）
    ↓  DynamicFlow 解释执行
Engine（SequenceExecutor）—— 框架，NuGet 可引用
    ↓  调用行业命令
StationExtension —— 你要复制的「行业插件」样板
    ApplyRecipe   → 写入限值/仿真值（SetShared）
    ProbeChannel  → 模拟仪表读数（可换成真表）
    MarkComplete  → 工位收尾
    ↓
Calculate / Assert —— 框架内置，判据写在 JSON 里
    ↓
OverallSuccess = true/false
```

**三层名字（知道即可，不必背）**

| 名字 | 是什么 | 本 Demo 里对应 |
|------|--------|----------------|
| L-DSL | 配方与变量 | `DynamicFlow` JSON + `Calculate`/`Assert` |
| L-Test | 跑测会话 | `SequenceExecutor`（`Program.cs` 里 Build） |
| L-Adapter | 行业/设备 | `ZL.Gear.Extension.Station` |

---

## 3. 命令速查（客户开发者 vs 维护者）

在仓库根目录执行（`dotnet run` 与参数之间必须有 `--`）：

```bash
# ① 第一次必跑（约 10 秒）
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- quickstart

# ② 深度学习：6 步 PASS，建立完整心智模型（约 2 分钟）
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- learn

# ③ 产品演示橱窗（5 条代表场景）
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- showcase

# ④ 能力矩阵 +「框架还有啥」地图
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- capabilities

# ⑤ 单场景调试（建议加 --report）
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- --report run Gear_Assert_L2_Condition

# ⑥ 列出全部 JSON
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- list
```

| 命令 | 给谁 | 说明 |
|------|------|------|
| `welcome` / 无参 | 所有人 | 只打印说明，**不跑场景** |
| `quickstart` | 客户 | 1 条合格路径 + 步骤树 |
| `learn` | 客户 | 6 步学习路径（见 [`CAPABILITIES.md`](CAPABILITIES.md)） |
| `showcase` | 产品演示 | 5 条 PASS |
| `verify` | **CI/维护者** | 7 条门禁（含 2 条故意 FAIL） |
| `bootstrap-demo` | 文档链接 | 指向 **ZL.Gear.Demos** `SeatHostBootstrap`（IndustryKit 已无 Seat 代码 · G3-01 ✅） |

无参数 `dotnet run ...` 等价于 `welcome`，避免误触 verify。

**座椅产线迁移（legacy StepConfig 树）**：公开 IndustryKit **仅**演示 `StationExtension` + LogicOnly verify（178）。盐城座椅 Bootstrap（Noise 注册、`sbrLocDict`、Verify 策略）属 **Demos/Exts**，见 `ZL.Gear.Docs/174` §五 · `175` §十五/§十八 · `177` Batch 5。

> IndustryKit **不含** Seat/SBR Bootstrap 源码；盐城产线入口见 `ZL.Gear.Demos/.../Seat/SeatHostBootstrap.cs`（180 G3）。

---

## 4. 打开哪一个 JSON？

| 你的目标 | 先打开 | 说明 |
|----------|--------|------|
| 理解**框架**能做什么（并行/重试/等待/断言） | `Scenarios/Gear_Core_Showcase.json` | **无**行业 Handler |
| Assert **L2 Condition** 方言 | `Scenarios/Gear_Assert_L2_Condition.json` | 与 L1 Check / L0 互斥（docs/006） |
| 理解**行业扩展**怎么接 | `Scenarios/Station_HappyPath.json` | 最短合格路径，注释在 `Description` 字段 |
| 只改配方、不改代码 | `Scenarios/Fork_Seatbelt_Like.json` | 复制后改 `RecipeId` / 限值 |
| 接真实仪表 | 对照 `ProbeChannelHandler.cs` | 保持 `MeasuredOhm` 变量名不变 |

场景文件旁有 [`Scenarios/README.md`](ZL.Gear.Samples.Industry.Client/Scenarios/README.md) 逐文件说明。

---

## 5. 复制到你的项目（最小清单）

| 复制 / 引用 | 来源 | 说明 |
|-------------|------|------|
| NuGet | `ZL.Gear.Core` + `ZL.Gear.Engine` | 框架内核 |
| 宿主样板 | `Program.cs` 里 `SequenceExecutorBuilder` 一段 | `AsLogicOnlyDemoHost()` + `WithExtension` |
| 行业扩展 | 整个 `ZL.Gear.Extension.Station/` | 改名为 `Extension.YourIndustry`，改命令前缀 |
| 配方 | `Scenarios/*.json` | 放到你的输出目录或配置路径 |

**宿主最小代码**（从 IndustryKit `Program.cs` 提炼，嵌进你的 WinForms/服务/CLI）：

```csharp
using var executor = SequenceExecutorBuilder.Create()
    .AsLogicOnlyDemoHost()                    // 无仪器；产线用 AsInstrumentedHost(...)
    .WithBuiltInModules(BuiltInModules.Core)  // Calculate/Assert/DynamicFlow
    .WithExtension(new StationExtension())    // 换成你的 IGearExtension
    .Build();

var steps = ScenarioLoader.Load("path/to/Station_HappyPath.json");
var result = await executor.ExecuteAsync(steps, model: "MyLine", barcode: "SN001");
// result.OverallSuccess → 上报 MES / UI
```

**不要复制**：`VerificationCatalog.cs`（CI 门禁）、`verify.sh`（除非你也做发版门禁）。

### 5.1 仅 NuGet、未 clone 本仓库

若你只安装了包、没有 IndustryKit 目录：

```bash
dotnet add package ZL.Gear.Core
dotnet add package ZL.Gear.Engine
```

1. 在解决方案中新建类库 `YourIndustry.Extension`（引用 Core），实现 `IGearExtension` + 三个 Handler（可参考 GitHub 上 `demos/IndustryKit/ZL.Gear.Extension.Station`）。  
2. 新建控制台/WinForms 宿主，粘贴上文 **宿主最小代码**，把 `StationExtension` 换成你的扩展。  
3. 将 `Station_HappyPath.json` 复制到输出目录 `Scenarios/`（或任意路径，`ScenarioLoader.Load` 传绝对路径）。  
4. 本地验证：`dotnet run -- run path/to/Station_HappyPath.json`（若你复制了 IndustryKit Client 的 `run` 命令逻辑）。

> 完整可运行样板仍需从本仓库 `demos/IndustryKit/` 复制；NuGet 只提供框架内核，不含 Demo 工程。

---

## 6. 三条铁律（产线安全，docs/004 展开）

1. **限值在 JSON Args**，Handler 用 `ArgsOnly` + `TryRequire*`，禁止 `context.Get` 静默兜底。  
2. **判据在 JSON Assert**，Handler 不做 `if (measured > limit) return Fail`。  
3. **流程变量用 `SetShared`**，禁止 Handler 里 `Variables.Set`（后继步骤读不到）。

---

## 7. 常见困惑

| 困惑 | 答案 |
|------|------|
| `verify` 为什么有 FAIL？ | 那是**故意**的失败用例，证明 Assert/超时不会假绿；客户日常用 `quickstart`/`showcase`，CI 才跑 `verify`。 |
| `showcase` 和 `verify` 区别？ | showcase = 产品演示 5 条；verify = 发版门禁 7 条（含 2 条必须失败）。 |
| 和 OpenTAP 比？ | 见根目录 [`README.md`](../../README.md)「与 OpenTAP / TestStand 的定位差异」。 |
| 真实仪器在哪？ | 公开 MIT 轨**不含** Drivers；Instrumented 路径在私有仓，IndustryKit 用仿真演示扩展缝。 |

---

## 8. 读完之后

- 改配方：复制 `Fork_Seatbelt_Like.json` → `dotnet run ... -- run YourScenario`  
- 加行业动作：复制 Extension 工程 → 实现新 `ActionKey` → JSON 引用  
- 能力全景：[`CAPABILITIES.md`](CAPABILITIES.md)（IndustryKit 已演示 vs Instrumented 能力）
- 深入规范：[`docs/004`](../../docs/004_行业扩展模板与使用场景_2026-09-12.md) · [`docs/005`](../../docs/005_StepArgsReader使用规范_2026-09-12.md) · [`docs/006`](../../docs/006_表达式变量与判定方言_2026-09-12.md) · [`docs/007`](../../docs/007_验收门禁与测试指南_2026-09-12.md)
