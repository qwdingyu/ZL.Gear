# demos/

公开 MIT 轨演示代码。当前仅 **IndustryKit**（行业扩展 + JSON 配方 + CLI 宿主）。

| 入口 | 说明 |
|------|------|
| [IndustryKit/GETTING_STARTED.md](IndustryKit/GETTING_STARTED.md) | 客户开发者 5 分钟上手 |
| [IndustryKit/CAPABILITIES.md](IndustryKit/CAPABILITIES.md) | 全系统能力全景（已演示 vs Instrumented） |
| [IndustryKit/README.md](IndustryKit/README.md) | 维护者概览 |

```bash
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- quickstart
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- learn
```

Instrumented 全栈场景在私有仓 `ZL.Gear.Demos`（ConsoleApp）。
