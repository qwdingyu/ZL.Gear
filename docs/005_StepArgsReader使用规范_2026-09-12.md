# StepArgsReader 使用规范

> **日期**：2026-09-12  
> **来源**：`ZL.Gear.Docs/140`  
> **用途**：Handler 参数读取统一规范、作用域规则、踩坑防护。

---

## 一、为什么需要 StepArgsReader

| 手写写法 | 风险 |
|----------|------|
| `context.Get("LimitOhm", 5.0)` | Variables/Global 静默兜底 → **产线漏检** |
| `step.Parameters.ContainsKey` + `Variables.TryGet` 混写 | 几十个 Handler 重复样板 |
| `Variables.Set` | 写节点级 → 后继 Assert **读不到**（作用域漂移） |

`StepArgsReader` = 统一 **来源策略 + 类型转换 + 中文错误 + commandLabel**。

---

## 二、StepArgSource 总表（读）

| 来源 | 枚举 | 查找顺序 | 典型用途 | 禁止误用 |
|------|------|----------|----------|----------|
| 仅本步 Args | `ArgsOnly` | `step.Parameters` 须含键 | 配方/限值 fail-closed | 读前序 SimulatedOhm |
| Args → Variables | `ArgsThenVariables` | Parameters → Variables 链 | 本步可覆盖，否则读前序 | 产线必填限值 |
| Args → Variables → Global | `All` | 同 `context.Get` | 通用可选、兼容旧代码 | **限值/RecipeId 必填** |
| 仅流程 Variables | `VariablesOnly` | Variables 链（含父级 flow） | MarkComplete 读 RecipeId | 读本步 Args 覆盖 |
| 仅 GlobalContext | `GlobalOnly` | GlobalContext | 条码、型号、操作员 | 工艺变量 |

---

## 三、API 速查表（读 / 写）

| API | 来源默认 | 失败行为 | 场景 |
|-----|----------|----------|------|
| `TryRequireString` | 调用方指定 | 返回 false + error | Args 必填字符串 |
| `TryRequirePositiveDouble` | 调用方指定 | 返回 false + error | Args 必填正数限值 |
| `TryGetDouble` | 调用方指定 | 返回 false + error | 可选/链式回退 |
| `GetOptionalString/Double` | `All` | 返回 defaultValue | 可选参数 |
| `GetFlowString/Double` | `VariablesOnly` | 返回 default（默认 `"?"` / `0`） | 读前序 SetShared |
| `TryRequireFlowString` | `VariablesOnly` | 返回 false + error | 收尾必须有 RecipeId |
| `GetGlobalString` | GlobalContext | 返回 defaultValue | 条码/型号 |
| `SetShared` | — | 写流程级 | 后继节点/Assert 可读 |
| `Fail(message)` | — | `ExecutionResult.Failed` | early return |

**创建：**

```csharp
var args = StepArgsReader.From(step, context, "ApplyRecipe");
```

`commandLabel` 出现在所有错误信息前缀。

---

## 四、写：SetShared 与作用域（防漂移）

对齐 `ContextVariableStore`：

```
Session Variables
  └─ flowVariables（DynamicFlow 流程级）
        └─ nodeVariables（单节点，Handler 在此执行）
```

| 写法 | 写入层 | 后继可读 | Handler 应用 |
|------|--------|----------|--------------|
| `args.SetShared(k,v)` | **直接父级**（通常 flowVariables） | ✅ | **唯一推荐** |
| `context.Variables.Set(k,v)` | 当前 nodeVariables | ❌ | **禁止**（Engine 内 Demo 除外） |
| `SetVariable` / `Calculate`（JSON） | SetShared → flow | ✅ | 配方首选 |

**SetShared 不写入：** GlobalContext、Session 绝对根、信令根（`$SYS:SIG:*` 走专用 API）。

---

## 五、污染分级与对策

| 级别 | 现象 | 原因 | 对策 |
|------|------|------|------|
| **P0 漏检** | 限值从 Variables 旧值兜底 | `context.Get` / `All` | 必填用 `ArgsOnly` + `TryRequire*` |
| **P1 漂移** | 后继 Assert 读不到 | `Variables.Set` 写节点级 | 只用 `SetShared` |
| **P2 串键** | Global 与工艺同名 | `All` 读到 Barcode 同名键 | 工艺用 `ArgsOnly`/`VariablesOnly`；会话用 `GlobalOnly` |
| **P3 并行** | Parallel 两分支写同键 | SetShared 末写覆盖 | Parallel 内用不同键或改 Sequence |
| **P4 注入** | flow 启动注入整包 Parameters | `WorkflowDefinition` 等大对象进 flowVariables | 表达式勿裸用 `WorkflowDefinition` 作变量名 |

---

## 六、踩坑清单（验收必查）

1. **不要用 `context.Get` 读产线限值** → 用 `ArgsOnly`。
2. **不要在 Handler 里 `Variables.Set`** → 用 `SetShared`。
3. **日志读前序状态** → `GetFlowString`，不是 `GetOptionalString(..., All)`。
4. **条码/型号** → `GetGlobalString` 或 `GlobalOnly`，不要与工艺键混名。
5. **`ParameterSchema` 字符串** → 目前**不自动绑定**（docs/121 §十二 明确不做自动解析）；须手写 `TryRequire*` 并与 schema 字段人工对齐。
6. **Parallel + 同键 SetShared** → 视为未定义竞态；配方应避。
7. **Args 键存在但值为 null** → `ArgsThenVariables` 会回退 Variables（已修正）；JSON 勿写 `"Key": null` 若意图走回退。
8. **DynamicFlow 外直接调 Handler** → 无 node 子作用域时 SetShared 写当前 store；行业 Handler 应在 DynamicFlow 内调用。
9. **AiDecision** → 已于 2026-09-11 改为 `SetShared`（`AiDecisionScopeTests`）；其它 Engine 内置 OutputKey 路径仍为有意契约。
10. **单测构造 StepContext** → 须非 null `IServiceProvider`（公开轨用 `tests/ZL.Gear.Testing.Common/StepContextFactory`）。
11. **可选 Args 覆盖** → 键存在则必须解析成功，禁止 `TryGet(..., out _)` 失败后静默回退（见 `ProbeChannelHandler`）。
12. **Assert L1 `Check`** → 仅单条件（`Left Op Right`）；禁止 `&&`/`||`/括号；多条件拆多条 Assert（`AssertCheckParser`）。
13. **`WorkflowDefinition.Variables` 预置限值** → 若 Handler 误用 `All`，会绕过 Args 校验（D-04）；限值应只来自 Args。
14. **Sampling 废弃字段 `Quantity`** → 须 `SampleCount`/`SampleIntervalMs`（`SamplingConfigModel`）；`ScenarioDemoLibraryTests.Demo库_Sampling块_不得使用废弃Quantity字段` 会门禁。

---

## 七、与 StepContextExtensions 分工

| 能力 | 使用 |
|------|------|
| LCL/UCL/Offset/Unit | `context.GetLcl()` 等 |
| 设备 | `context.GetDevice<T>()` |
| 信令 | `Variables.TryGetSignalPair` |
| 工艺 Args/Flow/Global | **StepArgsReader** |
| 表达式 `${}` | `context.ResolveText`（JSON Log） |

---

## 八、测试锚点

| 测试 | 文件 |
|------|------|
| StepArgsReader 单元 | `tests/ZL.Gear.Core.Tests/StepArgsReaderTests.cs` |
| Handler 产线合规 | `tests/ZL.Gear.Core.Tests/IndustryKitHandlerComplianceTests.cs` |
| 扩展加载 | 私有仓 `PluginChainLoadTests` |
| 行业闭环 | `demos/IndustryKit/verify.sh` → `INDUSTRY_KIT_VERIFY_PASS`（7 条） |
| Args 覆盖范例 | `ProbeChannelHandler.cs` + `Station_ProbeOverride.json` |

---

## 九、修订记录

| 日期 | 变更 |
|------|------|
| 2026-09-10 | 初版：总表 + 污染分级 + 踩坑 |
| 2026-09-10 | 增补 GlobalOnly / GetGlobalString；ArgsThenVariables null 回退 |
| 2026-09-11 | §五 ProbeChannel 模式更新；§六 增 11–14（可选 Args fail-closed、Assert 单条件、Agent dotnet 踩坑）；链 docs/141 §十 |
| 2026-09-11 | §六 增 15（Sampling 禁 `Quantity`）；与 `ScenarioDemoLibraryTests` 对齐 |
| 2026-09-12 | 公开轨整理；锚点改为 `demos/IndustryKit`；ApplyRecipe SimulatedOhm 正数校验 |
