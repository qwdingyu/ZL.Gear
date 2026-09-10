# ExprDialectProof — docs/133 回归门禁

> **目的**：对正式 `WorkflowEvaluator` + `StandardActionsProvider`（Assert L0/L1）做回归；须输出 `PROOF_ALL_PASS`。  
> 覆盖矩阵见 [COVERAGE.md](./COVERAGE.md)。

## 运行

```bash
cd /Users/dingyuwang/0-X/ZL.Gear
rtk dotnet build ZL.Gear.sln -v q
rtk dotnet build tools/ExprDialectProof/ExprDialectProof.csproj -c Release -v q
rtk dotnet run --project tools/ExprDialectProof/ExprDialectProof.csproj -c Release --no-build
# 期望末行：PROOF_ALL_PASS
# 工程已 ProjectReference Core/Engine，勿再依赖 bin/Debug HintPath
```

## Fixture

| 文件 | 作用 |
|------|------|
| `Fixtures/Showcase_NewDialect.json` | 新方言全量橱窗（与正式 `Demo_Core_Showcase` 对齐） |
| `Fixtures/Timeout_Contract.json` | 流程超时 Failed + Finally |

## 与正式代码关系

本工具**不再**内嵌求值器/Assert 副本；直接引用 `ZL.Gear.Core` / `ZL.Gear.Engine` 已构建 DLL。
