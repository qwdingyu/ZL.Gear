#!/usr/bin/env bash
# IndustryKit 闭环验证门禁
# 公开轨：LogicOnly 无授权 bypass（G0-4 · docs/163 PR-3）
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

echo "[IndustryKit] build..."
dotnet build demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj -c Release -v q

echo "[IndustryKit] verify..."
OUT="$(dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj -c Release --no-build -- verify)"
echo "$OUT" | tail -n 20
echo "$OUT" | grep -q "INDUSTRY_KIT_VERIFY_PASS"
echo "[IndustryKit] OK"
