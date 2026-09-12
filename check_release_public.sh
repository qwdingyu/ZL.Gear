#!/usr/bin/env bash
# ZL.Gear 公开轨质量门（仅 MIT 范围，不依赖 sibling 私有仓）
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

echo "===================================================="
echo "ZL.Gear 公开轨质量门"
echo "===================================================="

echo "[0/6] public-guard (G0) ..."
bash scripts/public-guard.sh

echo "[1/6] dotnet build ZL.Gear.sln ..."
dotnet build ZL.Gear.sln -c Release -v q

echo "[2/6] 公开轨单元测试 (Core + Engine + Extensions.Data) ..."
dotnet test tests/ZL.Gear.Core.Tests/ZL.Gear.Core.Tests.csproj -c Release -v q --no-build
dotnet test tests/ZL.Gear.Engine.Tests/ZL.Gear.Engine.Tests.csproj -c Release -v q --no-build
dotnet test tests/ZL.Gear.Extensions.Data.Tests/ZL.Gear.Extensions.Data.Tests.csproj -c Release -v q --no-build

echo "[3/6] IndustryKit verify (零 bypass) ..."
bash demos/IndustryKit/verify.sh

echo "[4/6] ExprDialectProof ..."
dotnet build tools/ExprDialectProof/ExprDialectProof.csproj -c Release -v q
PROOF_OUT="$(dotnet run --project tools/ExprDialectProof/ExprDialectProof.csproj -c Release --no-build 2>&1)"
echo "$PROOF_OUT" | tail -n 5
echo "$PROOF_OUT" | grep -q "PROOF_ALL_PASS" || {
  echo "❌ ExprDialectProof 未输出 PROOF_ALL_PASS"
  exit 5
}

PACK_DIR="$(mktemp -d)"
trap 'rm -rf "$PACK_DIR"' EXIT
echo "[5/6] dotnet pack (5 NuGet packages, no obfuscation) ..."
dotnet pack ZL.Gear.sln -c Release -o "$PACK_DIR" --no-build -v q
PKG_COUNT="$(find "$PACK_DIR" -maxdepth 1 -name '*.nupkg' | wc -l | tr -d ' ')"
if [ "$PKG_COUNT" != "5" ]; then
  echo "❌ 期望 5 个 .nupkg，实际 $PKG_COUNT"
  ls -la "$PACK_DIR" || true
  exit 6
fi
echo "  ✓ $(basename "$PACK_DIR"/*.nupkg 2>/dev/null | head -5 | tr '\n' ' ')"

echo "[6/6] 完成"
echo "✅ 公开轨质量门通过"
