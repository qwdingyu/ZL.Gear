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

echo "✅ public-guard 通过"
