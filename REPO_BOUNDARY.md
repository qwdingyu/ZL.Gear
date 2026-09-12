# ZL.Gear 仓库边界（公开 MIT 轨）

> **工作区**：`/Users/dingyuwang/0-X/ZL.Gear.All/`  
> **完整分仓策略**：私有仓 `ZL.Gear.Docs` · [167_五仓分仓与Gear.All工作区指南](https://github.com/qwdingyu/ZL.Gear.Docs/blob/main/167_五仓分仓与Gear.All工作区指南_2026-09-12.md)

## 本仓（ZL.Gear）包含

| 路径 | 说明 |
|------|------|
| `src/ZL.Gear.{Core,Engine,Sensing,Abstractions,Extensions.Data}` | 框架 DL · MIT |
| `tests/ZL.Gear.Extensions.Data.Tests` | 公开 CI 测试 |
| `check_release_public.sh` | 公开轨质量门（不含 Drivers/Demos） |
| `scripts/public-guard.sh` | G0 边界静态门禁 |
| `.github/workflows/public-ci.yml` | GitHub Actions 公开轨 CI |
| `.github/workflows/publish.yml` | NuGet 发布（tag `v*` / 手动；**无混淆**） |
| `pipeline.json` | 发布清单（5 包 · `obfuscate: false`） |
| `demos/IndustryKit/` | MIT 教学模板（仅引 Core/Engine） |
| `tools/ExprDialectProof` | 表达式方言门禁 |
| `LICENSE` | MIT（公开轨 G0-8） |

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

## 公开轨门禁（docs/163 G0 摘要）

```bash
# G0-1：Engine 无授权门禁
! rg LicenseGuard src/ZL.Gear.Engine
! rg 'ZL\.License' src/ZL.Gear.Engine/*.csproj

# G0-4：IndustryKit 零 bypass（无 ZL_LICENSE_* / ZL_GEAR_LICENSE_*）
env -i PATH="$PATH" HOME="$HOME" bash demos/IndustryKit/verify.sh

# G0-6：无 Drivers/HSL
! rg -l HslCommunication --glob '*.csproj' .
! rg -l 'ZL\.Gear\.Drivers' --glob '*.csproj' .
test ! -d ZL.Gear.Drivers

# G0-5
bash check_release_public.sh
```

**商业授权门禁**在 sibling 私有仓 `ZL.Gear.Drivers`（Instrumented 路径），不在本公开 Engine。

## 本地全栈联调

```bash
cd .. && dotnet build ZL.Gear.Full.sln
```
