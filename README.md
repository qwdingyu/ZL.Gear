# ZL.Gear

.NET 工业自动化微编排框架（Core / Engine / Sensing / Abstractions / Extensions.Data）。

**许可证**：MIT（见 [`LICENSE`](LICENSE)）。LogicOnly 公开轨无 Engine 授权门禁；Instrumented 授权在私有 `ZL.Gear.Drivers`。

## 快速开始

```bash
dotnet build ZL.Gear.sln
bash check_release_public.sh   # 含 scripts/public-guard.sh (G0)
```

全栈私有回归（ConsoleApp + Drivers）在 sibling 仓 **`ZL.Gear.Demos/check_release.sh`**（Gear.All 工作区）。

## 范围

本仓库仅含 MIT 公开轨。行业驱动、ConsoleApp、内部文档位于 sibling 私有仓；边界见 [`REPO_BOUNDARY.md`](REPO_BOUNDARY.md)。

## 教学模板

[`demos/IndustryKit/`](demos/IndustryKit/) — 无硬件、无 Drivers 的 LogicOnly 行业扩展示范。
