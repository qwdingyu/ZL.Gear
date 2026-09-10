# ExprDialectProof — docs/133 独立论证项目

> **目的**：不改正式 Core/Scenarios，验证「过滤裸标识符 + 结构化 Assert」。  
> **门禁**：必须输出 `PROOF_ALL_PASS`。覆盖矩阵见 [COVERAGE.md](./COVERAGE.md)。

## 运行

```bash
cd /Users/dingyuwang/0-X/ZL.Gear
rtk dotnet build tools/ExprDialectProof/ExprDialectProof.csproj -v q
rtk dotnet run --project tools/ExprDialectProof/ExprDialectProof.csproj --no-build
```

## Fixture

| 文件 | 作用 |
|------|------|
| `Fixtures/Showcase_NewDialect.json` | 对齐正式橱窗结构的新方言全量路径 |
| `Fixtures/Timeout_Contract.json` | 流程超时 Failed + Finally |

## 与正式代码关系

本目录内求值器/Assert 为**论证副本**。`PROOF_ALL_PASS` 后才允许迁入 Core/Engine 并批量改 JSON。
