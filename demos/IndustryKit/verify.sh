#!/usr/bin/env bash
# IndustryKit 闭环验证门禁
# CI/Dev 授权：与 check_release.sh / TestSetup 一致（docs/141 §10.5(4)）
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

# Release 构建下 LicenseConfig 强制 DevMode=false；CI/verify 轨与 TestSetup 一致绕过 LicenseGuard
export ZL_GEAR_LICENSE_TEST_MODE="${ZL_GEAR_LICENSE_TEST_MODE:-true}"

echo "[IndustryKit] build..."
dotnet build demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj -c Release -v q

echo "[IndustryKit] verify..."
OUT="$(dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj -c Release --no-build -- verify)"
echo "$OUT" | tail -n 20
echo "$OUT" | grep -q "INDUSTRY_KIT_VERIFY_PASS"
echo "[IndustryKit] OK"
