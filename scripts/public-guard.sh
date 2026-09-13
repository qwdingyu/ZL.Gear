#!/usr/bin/env bash
# 公开 ZL.Gear 仓库边界门禁（docs/163 G0-1 / G0-4 / G0-6）
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

fail() { echo "❌ G0 门禁失败: $*" >&2; exit 1; }

echo "=== public-guard (G0) ==="

if grep -rqn "LicenseGuard" --include="*.cs" src/ZL.Gear.Engine 2>/dev/null; then
  fail "src/ZL.Gear.Engine 仍含 LicenseGuard"
fi
if grep -q 'ZL\.License' src/ZL.Gear.Engine/ZL.Gear.Engine.csproj 2>/dev/null; then
  fail "Engine csproj 仍引用 ZL.License"
fi
if grep -rl "HslCommunication" --include="*.csproj" . 2>/dev/null; then
  fail "csproj 仍引用 HslCommunication"
fi
if grep -rl 'ZL\.Gear\.Drivers' --include="*.csproj" . 2>/dev/null; then
  fail "csproj 仍引用 ZL.Gear.Drivers"
fi
if grep -rlE 'ZL\.Gear\.Exts|ZL\.Gear\.Ext\.' --include="*.csproj" . 2>/dev/null; then
  fail "csproj 仍引用私有行业包（ZL.Gear.Exts / ZL.Gear.Ext.*）"
fi
if test -d ZL.Gear.Drivers; then
  fail "仓库内不应存在 ZL.Gear.Drivers 目录"
fi
if test -d ZL.Gear.Exts; then
  fail "仓库内不应存在 ZL.Gear.Exts 目录"
fi
# G0-6b：IndustryKit 源码不得 using 私有 Drivers/Exts（仅文档字符串可提及仓名）
INDUSTRY_KIT_SRC="demos/IndustryKit"
if grep -rnE '^using ZL\.Gear\.(Drivers|Ext\.)' --include="*.cs" "$INDUSTRY_KIT_SRC" 2>/dev/null; then
  fail "IndustryKit 源码仍 using 私有 Drivers/Exts 命名空间"
fi
if grep -rnE 'ProjectReference.*ZL\.Gear\.(Drivers|Exts|Ext\.)' --include="*.csproj" "$INDUSTRY_KIT_SRC" 2>/dev/null; then
  fail "IndustryKit csproj 仍 ProjectReference 私有 Drivers/Exts"
fi
if grep -q 'ZL_GEAR_LICENSE_TEST_MODE' check_release_public.sh demos/IndustryKit/verify.sh 2>/dev/null; then
  fail "公开 CI 脚本仍含 TestMode bypass"
fi

# G0-7：IndustryKit verify 依赖的文件须全部 git 跟踪（防「本地绿、CI 灾难」）
INDUSTRY_KIT_REQUIRED=(
  demos/IndustryKit/verify.sh
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/Program.cs
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/VerificationCatalog.cs
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/CapabilityCatalog.cs
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ShowcaseCatalog.cs
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/RunReportPrinter.cs
  demos/IndustryKit/ZL.Gear.Extension.Station/StationExtension.cs
  demos/IndustryKit/ZL.Gear.Extension.Station/Handlers/ApplyRecipeHandler.cs
  demos/IndustryKit/ZL.Gear.Extension.Station/Handlers/ProbeChannelHandler.cs
  demos/IndustryKit/ZL.Gear.Extension.Station/Handlers/MarkCompleteHandler.cs
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/Scenarios/Gear_Core_Showcase.json
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/Scenarios/Station_HappyPath.json
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/Scenarios/Station_ProbeOverride.json
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/Scenarios/Station_Integrated_Ate.json
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/Scenarios/Station_AssertFail.json
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/Scenarios/Station_TimeoutContract.json
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/Scenarios/Fork_Seatbelt_Like.json
)
for f in "${INDUSTRY_KIT_REQUIRED[@]}"; do
  if ! git ls-files --error-unmatch "$f" >/dev/null 2>&1; then
    fail "IndustryKit 必需文件未纳入 git: $f（verify 在干净 clone 会失败）"
  fi
done

echo "=== G-C / G0-10 (179 §6.3 · 178 §六) ==="

# G-C1：GlobalEvents 无 static Action/Func（legacy UI 回调已迁 LegacyBridge）
if grep -qE 'public static (Action|Func)' src/ZL.Gear.Core/Events/GlobalEvents.cs 2>/dev/null; then
  fail "G-C1: GlobalEvents 仍含 static Action/Func"
fi

# 豁免登记（2026-09-13 · 184 §五 P1-2）：
#   ZL.Gear.Sensing/SensingLog.cs 的 static Action（Default/Debug/Info/Warn/Error）为
#   「通用日志门面」，非 UI/PLC/行业订阅端口，不在 G-C1 语义范围；G-C1 仅约束 GlobalEvents.cs。
#   若未来扩展 G-C1 词表覆盖 Sensing，必须在此显式豁免，避免误报。

# G-C2：Core 无 ParseSensorSpecs / 中文传感器键解析
if rg -l 'ParseSensorSpecs|class SensorSpec' src/ZL.Gear.Core --glob '*.cs' 2>/dev/null; then
  fail "G-C2: Core 仍含 ParseSensorSpecs 或 SensorSpec"
fi

# G-C4：Core 无 CAN/UDS 自学习 UI 专用 DTO
if rg -l 'KeyTrafficLogModel' src --glob '*.cs' 2>/dev/null; then
  fail "G-C4: src 仍含 KeyTrafficLogModel"
fi

# G-C5：无公开 CustomerParam 属性（JSON 私有桥接 Parameters 允许）
if rg 'public\s+\S+\s+CustomerParam\b' src --glob '*.cs' 2>/dev/null; then
  fail "G-C5: src 仍含公开 CustomerParam 属性"
fi

# G-C6：不得保留固化行业 static 事件的兼容测试
if find src tests -name '*LegacyCompatibilityTests*' 2>/dev/null | grep -q .; then
  fail "G-C6: 仍存在 LegacyCompatibilityTests"
fi

# G0-10 / G-C3：公开 src 禁止行业关键词（tests 白名单除外）
# 词表口径（2026-09-12 终态追加）：
#  - 覆盖 座椅/SBR/电检/盐城/自学习/CAN自学习UI/PLC↔UI桥/NoiseService 等全部历史污染词根；
#  - 追加 ModelStepService|PFLite|Dzjdq|Dljdq|Frm_Seat|SeatTest|AutoSbr|双手启动|TwoHandStart
#    （本轮实测零命中，防 legacy 词汇随迁移复入公开轨）；
#  - 有意不收录 PlcAutoManual/PlcLocation：已迁 Ext.Seat（G1d-04）；公开 Core 不含此类名。
G0_10_PATTERN='座椅|SBR|SeatCode|UiPlcEvents|ParseSensorSpecs|盐城|NoiseService|安全带|电检|靠背|座垫|自学习|卡扣|ModelStepService|PFLite|Dzjdq|Dljdq|Frm_Seat|SeatTest|AutoSbr|双手启动|TwoHandStart'
if rg -l "$G0_10_PATTERN" src --glob '*.cs' --glob '!**/tests/**' 2>/dev/null; then
  fail "G0-10: 公开 src 仍含行业关键词（见 179 §6.3 扩展词表）"
fi

# G-C8：DeviceNotifier 不得保留 static Action/Func（已收敛为 IEventBus typed event）
if rg -l 'public static (Action|Func)' src/ZL.Gear.Core/Events/DeviceNotifier.cs 2>/dev/null; then
  fail "G-C8: DeviceNotifier 仍含 static Action/Func"
fi

# G-C9：公开仓不得提供 Seat 命名 Bootstrap 类
if rg -l 'class Seat\w*Bootstrap|SeatProductionHostBootstrap' src demos --glob '*.cs' 2>/dev/null; then
  fail "G-C9: 公开仓仍含 Seat 命名 Bootstrap 类"
fi

# G-C10：公开 Sensing 不得绑定座椅 PLC 事件类型名（注释/代码 · G1d-03）
if rg -l 'PlcAutoManualEvent|PlcLocationEvent|UiPlcEvents' src/ZL.Gear.Sensing --glob '*.cs' 2>/dev/null; then
  fail "G-C10: Sensing 仍引用座椅 PLC 事件类型（须用泛型 IEvent + 宿主注入）"
fi

# P1-5：sln 引用完整性（路径存在 + 被 git 跟踪）——D1/D5 防复发
#   D1：Exts.sln 曾引用 ..\ZL.Gear\demos\SeatDemo（不存在）→ sln 无法加载
#   D5：Demos.sln 引用的 SeatDemo/InstrumentTest 曾未被 git 跟踪 → 干净 clone 构建失败
while IFS= read -r sln; do
  while IFS= read -r proj; do
    [ -n "$proj" ] || continue
    rel="${proj//\\//}"
    if [ ! -f "$rel" ]; then
      fail "P1-5: $sln 引用的项目不存在: $rel"
    fi
    if ! git ls-files --error-unmatch "$rel" >/dev/null 2>&1; then
      fail "P1-5: $sln 引用未跟踪项目: $rel"
    fi
  done < <(grep -oE '"[^"]+\.csproj"' "$sln" | tr -d '"' || true)
done < <(find . -maxdepth 1 -name '*.sln' | sort)

echo "✅ public-guard 通过"
