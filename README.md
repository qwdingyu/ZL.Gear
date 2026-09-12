# ZL.Gear

.NET 工业自动化微编排框架 —— Headless 测试序列执行器 + JSON 配方 DSL + 可插拔行业扩展。

**仓库边界**：本仓仅含 MIT 公开轨。行业驱动、ConsoleApp、内部文档位于 sibling 私有仓；边界见 [`REPO_BOUNDARY.md`](REPO_BOUNDARY.md)。

---

## 它解决什么痛点

### 1. 产线测试序列难以版本化与复用
传统自动化测试往往把步骤树、限值、判据散落在代码里。改一个参数要动代码、编译、部署，工程师无法独立维护配方。

**ZL.Gear 的做法**：把测试序列描述为 JSON（`DynamicFlow`），执行与评估分离。限值、判据、步骤顺序都在 JSON 里，运行期由引擎解释执行。

### 2. 通用工作流引擎太重，与电检域不匹配
通用 BPM / soft PLC / 工作流库设计的是「长期运行、人工干预、动态分支」，而产线 ATE 需要的是「单站闭环、超时硬约束、StopByFail、OverallSuccess 明确」。

**ZL.Gear 的做法**：三层架构（L-DSL 微流程 / L-Test 测试运行器 / L-Adapter 工业适配），每一层只做一件事，不强行通用。

### 3. 开源框架闭源授权，Demo 与框架耦合
许多测试框架把演示宿主、行业样例和内核捆在一起，导致：
- 想引用内核必须把整套 Demo 带进项目
- 教学模板里充斥着私有驱动、闭源 DLL、商业授权检查
- 新人无法从公开代码学习扩展点

**ZL.Gear 的做法**：公开 MIT 轨仅含框架内核 + IndustryKit 教学模板。行业驱动、仪器适配、商业授权全部放在私有仓，公开轨零授权门禁（LogicOnly）。

### 4. .NET 工业栈缺乏「执行器 + 评估器」分离的实现
常见模式是 Handler 里既读设备又做 Assert，导致：
- 同一 Handler 无法复用于不同限值
- 仿真与产线必须维护两套代码
- 缺参、超时、设备故障的优先级混乱

**ZL.Gear 的做法**：
- `StepArgsReader` + `ArgsOnly` 强制限值 fail-closed
- JSON `Assert` 做规格判定，Handler 只做设备动作与安全硬中断
- `ResultEvaluator` 统一评估，执行与评估彻底解耦

---

## 与 OpenTAP / TestStand 的定位差异

| 维度 | OpenTAP / TestStand | ZL.Gear |
|------|---------------------|---------|
| **形态** | 可视化设计器 + 插件生态 | Headless 执行器 + JSON 配方 DSL |
| **部署** | 重型 IDE / 服务 | NuGet 包 / 单文件宿主 |
| **扩展方式** | C# Step 插件（需编译） | `IGearExtension` + JSON ActionKey（配方可改） |
| **数据驱动** | Plan/Results 解耦 | `DynamicFlow` JSON + `SetShared` 变量流 |
| **授权** | Commercial | MIT（公开轨） |
| **适用场景** | 多站、多进程、复杂 UI | 单站 ATE、嵌入式宿主、无 UI 产线 |

ZL.Gear 学 OpenTAP/TestStand 的「形态」（Step 插件 + Plan 数据解耦 + 执行≠评估），但不学它的「重量」。如果你需要可视化设计器、插件市场、DUT 管理，ZL.Gear 不是答案。如果你需要一个可嵌入 .NET 产线软件、用 JSON 描述测试序列、Execution 和 Assert 分离的轻量执行内核，ZL.Gear 是选项。

---

## 架构概览

```text
┌─────────────────────────────────────────────────────────────┐
│  Demo 宿主（不发布 NuGet）                                    │
│  IndustryKit Client（公开 MIT 模板）                          │
└───────────────────────────┬─────────────────────────────────┘
                            │ 引用
┌───────────────────────────▼─────────────────────────────────┐
│  L-Test  测试运行器（框架 · 拟发布）                           │
│  ZL.Gear.Engine：SequenceExecutor / StepDispatcher /          │
│  ResultEvaluator / ModuleLoader / DynamicFlowHandler          │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│  L-DSL  微流程内核（框架 · 拟发布）                            │
│  ZL.Gear.Core：MicroWorkflow / WorkflowEvaluator / StepConfig │
│  / ContextVariableStore / StepArgsReader …                    │
└───────────────────────────┬─────────────────────────────────┘
                            │ 接口
┌───────────────────────────▼─────────────────────────────────┐
│  L-Adapter  业务/仪器适配（不随框架 NuGet 强绑 · 客户或内部）   │
│  Drivers · Communication · Seat · 行业 Extension · Sensing*   │
└─────────────────────────────────────────────────────────────┘
```

| 层 | 程序集 | 职责 | 不负责 |
|----|--------|------|--------|
| **L-DSL** | `ZL.Gear.Core` | 配方 DTO、变量、表达式、`StepArgsReader`、`IGearExtension` 接口 | 执行循环、驱动实例、UI、顶层 OverallSuccess |
| **L-Test** | `ZL.Gear.Engine` | 一次跑测会话：租约、步骤树、评测、报告、DynamicFlow 解释 | 具体 VISA/PLC 协议、行业配方语义、合格限值硬编码 |
| **L-Adapter** | `ZL.Gear.Sensing` + 私有仓 | 测量参考实现、设备驱动、行业 Handler | DSL 内核；禁止死绑进「通用 Workflow」 |

**关键设计决策**：
- Engine **无** `ZL.Gear.Drivers` 编译引用（Phase 1 解耦已完成）
- Build 强制显式宿主声明：`AsLogicOnlyDemoHost()` 或 `AsInstrumentedHost(deviceService)`
- 没有隐式 `NullDeviceService` 兜底 —— 未声明宿主直接 throw

---

## 快速开始

### 构建

```bash
dotnet build ZL.Gear.sln
bash check_release_public.sh   # G0 门禁 + 单元测试 + IndustryKit verify + pack 校验
```

### NuGet（轨道 A · MIT）

```bash
dotnet add package ZL.Gear.Core
dotnet add package ZL.Gear.Engine    # LogicOnly；Instrumented 驱动在私有 ZL.Gear.Drivers
# 可选：ZL.Gear.Abstractions · ZL.Gear.Sensing · ZL.Gear.Extensions.Data
```

发布：打 tag `vX.Y.Z` 触发 [`.github/workflows/publish.yml`](.github/workflows/publish.yml)（OIDC，无混淆）。

---

## 教学模板

[`demos/IndustryKit/`](demos/IndustryKit/) 是无硬件、无 Drivers 的 LogicOnly 行业扩展示范。

```bash
# 验证闭环（含故意 FAIL + 超时契约）
bash demos/IndustryKit/verify.sh
```

**新手必读**：
- 不要拷贝 `ZL.Gear.Extension.Seat`（私有 libs、现场 Tag 硬编码）
- 复制 `demos/IndustryKit/`，实现 `IGearExtension` + `RegisterHandlerWithAction`
- 配方走 DynamicFlow JSON，限值用 `ArgsOnly` + `TryRequire*`
- 判据在 JSON `Assert`，不在 Handler 内 if 限值

---

## 适用场景

| 场景 | 宿主类型 | 典型用户 |
|------|----------|----------|
| 纯逻辑 / 行业 Handler 示范（无真实仪器） | `AsLogicOnlyDemoHost()` | IndustryKit、教学模板 |
| 产线 / ConsoleApp / 真实仪器 | `AsInstrumentedHost(deviceService)` | 电检工程师、调试人员 |
| 全栈回归（Mock 场景） | `AsInstrumentedHost(mockService)` | CI / 发版门禁 |

---

## 不在本仓的内容

| 组件 | 仓 | 说明 |
|------|-----|------|
| `ZL.Gear.Drivers` | `../ZL.Gear.Drivers/` | 私有驱动（HSL、VISA、Mock） |
| `ZL.Gear.Exts` | `../ZL.Gear.Exts/` | 私有行业包（Seat 等） |
| `ZL.Gear.Demos` | `../ZL.Gear.Demos/` | 私有 Demo（ConsoleApp 全栈回归） |
| `ZL.Gear.Docs` | `../ZL.Gear.Docs/` | 私有文档（编号 028–167、商业分析） |

---

## 版本

当前公开轨 **v1.0.1**（MIT）。Instrumented 驱动与 ConsoleApp 场景库在 sibling 私有仓，不在本仓库。

---

## 许可证

MIT（见 [`LICENSE`](LICENSE)）。LogicOnly 公开轨无 Engine 授权门禁；Instrumented 授权在私有 `ZL.Gear.Drivers`。
