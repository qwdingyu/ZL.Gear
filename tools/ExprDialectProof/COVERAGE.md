# ExprDialectProof 覆盖清单（对照 docs/133 L0+L1 + Demo_Core_Showcase）

> 门禁：`PROOF_ALL_PASS`  
> 范围：表达式/判定方言；不含真机 ParallelMeasure/Read。

## A. Fixture `Showcase_NewDialect.json`

| 能力 | 状态 |
|------|------|
| Calculate 裸标识符 | ✅ |
| Assert **L1 Check**（`Margin > 0`、`LeakageMa <= MaxLeakage`） | ✅ |
| Assert **L0** 对照（Left/Op/Right） | ✅ |
| Retry / Parallel / Group / WaitUntil / Delay / Finalizers / `${}` | ✅ |

## B. Program 门禁

| ID | 内容 |
|----|------|
| U1–U7 | 提升过滤与类型 |
| F1–F3 | L0 成败与 Condition 双写 |
| F4–F6 | 全量橱窗 / 污染 Calculate / Timeout |
| F7–F13 | RightVar、Op 矩阵、字符串 Eq、Vars 逃逸、Calculate 失败、WaitUntil 超时、Right 互斥 |
| P1–P3 | Check 解析、拒算术、边界健壮性（无空格/科学计数/转义/中文 ident/超长） |
| F14–F16 | L1 Check 端到端+互斥；L0 Op 符号别名 `<=`；**空 Assert 不得误 PASS** |

## C. 延后

Sampling `val`、真机 ParallelMeasure — 不挡方言落地。行业官方 JSON 已切 Check/裸标识符（见 ConsoleApp Scenarios）。
