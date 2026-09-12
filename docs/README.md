# 文档

编号文档（028–167）、商业分析与顾问评审已迁至 **私有仓库 [ZL.Gear.Docs](https://github.com/qwdingyu/ZL.Gear.Docs)**。

本公开仓库保留以下公开文档（与 `demos/IndustryKit/`、`check_release_public.sh` 真值对齐）：

| 文档 | 用途 |
|------|------|
| **001_产品定位与核心功能** | 对外口径、三层产品面、NuGet 双轨 |
| **002_架构设计与核心组件** | 框架分层、Engine 解耦、显式宿主 |
| **003_Engine解耦与宿主策略** | Build 门禁、LogicOnly vs Instrumented |
| **004_行业扩展模板与使用场景** | IndustryKit：`verify` / `showcase` / Handler 规范 |
| **005_StepArgsReader使用规范** | ArgsOnly / SetShared / fail-closed |
| **006_表达式变量与判定方言** | Calculate / Assert L1·L0 / 作用域 |
| **007_验收门禁与测试指南** | 6 步公开轨 + 7 条 IndustryKit verify |
| **008_测试体系审查与优化指南** | tests/ 分层矩阵、去重策略、backlog |
| **009_测试用例审查与补充计划** | tests/ 补充批次记录、合规扫描、回归数字 |

**客户开发者快速入口**

```bash
# 5 分钟上手（见 demos/IndustryKit/GETTING_STARTED.md）
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- quickstart
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- showcase
```

**维护者 / 发版门禁**

```bash
bash check_release_public.sh
bash demos/IndustryKit/verify.sh
```

仓库边界见 [`REPO_BOUNDARY.md`](../REPO_BOUNDARY.md)。
