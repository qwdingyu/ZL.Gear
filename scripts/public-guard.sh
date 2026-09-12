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
if test -d ZL.Gear.Drivers; then
  fail "仓库内不应存在 ZL.Gear.Drivers 目录"
fi
if grep -q 'ZL_GEAR_LICENSE_TEST_MODE' check_release_public.sh demos/IndustryKit/verify.sh 2>/dev/null; then
  fail "公开 CI 脚本仍含 TestMode bypass"
fi

# G0-7：IndustryKit verify 依赖的文件须全部 git 跟踪（防「本地绿、CI 灾难」）
INDUSTRY_KIT_REQUIRED=(
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/CapabilityCatalog.cs
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ShowcaseCatalog.cs
  demos/IndustryKit/ZL.Gear.Samples.Industry.Client/RunReportPrinter.cs
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

echo "✅ public-guard 通过"
