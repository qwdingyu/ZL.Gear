#!/usr/bin/env bash
# IndustryKit 闭环验证门禁
# 公开轨：LogicOnly 无授权 bypass（G0-4 · docs/163 PR-3）
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

SCEN_DIR="demos/IndustryKit/ZL.Gear.Samples.Industry.Client/Scenarios"
for json in Gear_Core_Showcase.json Station_HappyPath.json Station_ProbeOverride.json \
  Station_Integrated_Ate.json Station_AssertFail.json Station_TimeoutContract.json Fork_Seatbelt_Like.json; do
  if [ ! -f "$SCEN_DIR/$json" ]; then
    echo "❌ 缺少 verify 场景: $SCEN_DIR/$json" >&2
    exit 3
  fi
done

echo "[IndustryKit] build..."
dotnet build demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj -c Release -v q

echo "[IndustryKit] verify..."
OUT="$(dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj -c Release --no-build -- verify)"
echo "$OUT" | tail -n 20
echo "$OUT" | grep -q "INDUSTRY_KIT_VERIFY_PASS"
echo "[IndustryKit] OK"
