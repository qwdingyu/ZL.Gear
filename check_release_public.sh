#!/usr/bin/env bash
# ZL.Gear 公开轨质量门（仅 MIT 范围，不依赖 sibling 私有仓）
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

export ZL_GEAR_LICENSE_TEST_MODE="${ZL_GEAR_LICENSE_TEST_MODE:-true}"

echo "===================================================="
echo "ZL.Gear 公开轨质量门"
echo "===================================================="

echo "[1/4] dotnet build ZL.Gear.sln ..."
dotnet build ZL.Gear.sln -c Release -v q

echo "[2/4] Extensions.Data 单元测试 ..."
dotnet test tests/ZL.Gear.Extensions.Data.Tests/ZL.Gear.Extensions.Data.Tests.csproj -c Release -v q

echo "[3/4] IndustryKit verify ..."
bash demos/IndustryKit/verify.sh

echo "[4/4] ExprDialectProof ..."
dotnet run --project tools/ExprDialectProof/ExprDialectProof.csproj -c Release --no-build 2>/dev/null || \
  dotnet run --project tools/ExprDialectProof/ExprDialectProof.csproj -c Release

echo "✅ 公开轨质量门通过"
