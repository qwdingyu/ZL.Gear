#!/usr/bin/env bash
# ZL.Gear 公开轨质量门（仅 MIT 范围，不依赖 sibling 私有仓）
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

echo "===================================================="
echo "ZL.Gear 公开轨质量门"
echo "===================================================="

echo "[0/5] public-guard (G0) ..."
bash scripts/public-guard.sh

echo "[1/5] dotnet build ZL.Gear.sln ..."
dotnet build ZL.Gear.sln -c Release -v q

echo "[2/5] Extensions.Data 单元测试 ..."
dotnet test tests/ZL.Gear.Extensions.Data.Tests/ZL.Gear.Extensions.Data.Tests.csproj -c Release -v q --no-build

echo "[3/5] IndustryKit verify (零 bypass) ..."
bash demos/IndustryKit/verify.sh

echo "[4/5] ExprDialectProof ..."
dotnet build tools/ExprDialectProof/ExprDialectProof.csproj -c Release -v q
PROOF_OUT="$(dotnet run --project tools/ExprDialectProof/ExprDialectProof.csproj -c Release --no-build 2>&1)"
echo "$PROOF_OUT" | tail -n 5
echo "$PROOF_OUT" | grep -q "PROOF_ALL_PASS" || {
  echo "❌ ExprDialectProof 未输出 PROOF_ALL_PASS"
  exit 5
}

echo "[5/5] 完成"
echo "✅ 公开轨质量门通过"
