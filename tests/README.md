# tests/ — 公开轨测试

真值源：`check_release_public.sh` 第 2 步。架构说明见 [`docs/008_测试体系审查与优化指南_2026-09-12.md`](../docs/008_测试体系审查与优化指南_2026-09-12.md)。

## 项目

| 目录 | 运行 |
|------|------|
| `ZL.Gear.Core.Tests` | `dotnet test tests/ZL.Gear.Core.Tests -c Release` |
| `ZL.Gear.Engine.Tests` | `dotnet test tests/ZL.Gear.Engine.Tests -c Release` |
| `ZL.Gear.Extensions.Data.Tests` | `dotnet test tests/ZL.Gear.Extensions.Data.Tests -c Release` |
| `ZL.Gear.Sensing.Tests` | `dotnet test tests/ZL.Gear.Sensing.Tests -c Release` |
| `ZL.Gear.Testing.Common` | （库，不单独跑） |

场景门禁：`bash demos/IndustryKit/verify.sh`（非本目录）。

## 共享库

`ZL.Gear.Testing.Common` 提供 `GearTestPaths`、`StepContextFactory`——**勿在各测试项目重复 Stub**。
