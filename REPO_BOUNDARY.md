# ZL.Gear 仓库边界（公开 MIT 轨）

> **工作区**：`/Users/dingyuwang/0-X/ZL.Gear.All/`  
> **完整分仓策略**：私有仓 `ZL.Gear.Docs` · [167_五仓分仓与Gear.All工作区指南](https://github.com/qwdingyu/ZL.Gear.Docs/blob/main/167_五仓分仓与Gear.All工作区指南_2026-09-12.md)

## 本仓（ZL.Gear）包含

| 路径 | 说明 |
|------|------|
| `src/ZL.Gear.{Core,Engine,Sensing,Abstractions,Extensions.Data}` | 框架 DL · MIT |
| `tests/ZL.Gear.Extensions.Data.Tests` | 公开 CI 测试 |
| `check_release_public.sh` | 公开轨质量门（不含 Drivers/Demos） |
| `demos/IndustryKit/` | MIT 教学模板（仅引 Core/Engine） |
| `tools/ExprDialectProof` | 表达式方言门禁 |
| `tests/LicenseEnvTest` | 授权环境测试（待 PR-A 后评估迁私有仓） |

## 本仓不包含（ sibling 私有仓）

| 仓 | 路径 |
|----|------|
| ZL.Gear.Drivers | `../ZL.Gear.Drivers/` |
| ZL.Gear.Exts | `../ZL.Gear.Exts/` |
| ZL.Gear.Demos | `../ZL.Gear.Demos/` |
| ZL.Gear.Docs | `../ZL.Gear.Docs/` |

## 历史清理（方案 A · 2026-09-12）

已执行 `git filter-repo`（清单见 `.filter-repo-paths-to-remove.txt`）。  
历史 blob 中 **0** 条 Drivers/HSL/私有路径命中。本地 `.git/filter-repo/` 导出缓存已删除。

## 公开 push 前门禁（G0-6 摘要）

```bash
! rg -l HslCommunication --glob '*.csproj' .
! rg -l 'ZL\.Gear\.Drivers' --glob '*.csproj' .
test ! -d ZL.Gear.Drivers
```

## 本地全栈联调

```bash
cd .. && dotnet build ZL.Gear.Full.sln
```
