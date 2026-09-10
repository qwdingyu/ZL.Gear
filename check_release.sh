#!/bin/bash

# ZL.Gear 发布质量门 (Quality Gate)
# 用途: 在发布新版本前，自动验证所有核心逻辑和经典场景。

echo "===================================================="
echo "🚀 ZL.Gear 自动化发布质量门检查启动..."
echo "===================================================="

# 1. 编译自检
echo "[1/5] 正在进行全项目编译自检..."
dotnet build ZL.Gear.sln -v q
if [ $? -ne 0 ]; then
    echo "❌ 编译失败！请检查代码错误。"
    exit 1
fi
echo "✅ 编译通过。"

# 2. 核心单元测试
echo "[2/5] 正在运行单元测试..."
dotnet test ZL.Gear.sln --no-build -v q
if [ $? -ne 0 ]; then
    echo "❌ 单元测试末通过！请检查逻辑回归。"
    exit 2
fi
echo "✅ 单元测试全部通过。"

# 3. 表达式方言门禁（docs/133）
echo "[3/5] 正在运行 ExprDialectProof（须 PROOF_ALL_PASS）..."
dotnet build ./tools/ExprDialectProof/ExprDialectProof.csproj -c Release -v q
if [ $? -ne 0 ]; then
    echo "❌ ExprDialectProof 编译失败！"
    exit 5
fi
PROOF_OUT=$(dotnet run --project ./tools/ExprDialectProof/ExprDialectProof.csproj -c Release --no-build 2>&1)
echo "$PROOF_OUT" | tail -n 5
if echo "$PROOF_OUT" | grep -q "PROOF_ALL_PASS"; then
    echo "✅ 表达式方言门禁通过。"
else
    echo "❌ ExprDialectProof 未输出 PROOF_ALL_PASS。"
    exit 5
fi

# 4. 经典集成场景自回归 (Mock 模式)
echo "[4/5] 正在运行经典集成场景 (Mock 模式)..."
export ZL_GEAR_FORCE_MOCK=true
# 运行完整集成测试场景
dotnet run --project ./ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj -- -s ./ZL.Gear.ConsoleApp/Scenarios/FullIntegrationTest.json --verbose > /tmp/zlgear_test.log 2>&1

if grep -q "测试通过 (PASS)" /tmp/zlgear_test.log; then
    echo "✅ 经典场景回归通过。"
else
    echo "❌ 场景执行异常！请查看日志 /tmp/zlgear_test.log"
    exit 3
fi

# 5. 关键特性验证 (护城河特性自检)
echo "[5/5] 正在验证护城河特性 (资源锁、重试、逻辑分支)..."
dotnet run --project ./ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj -- -s ./ZL.Gear.ConsoleApp/Scenarios/MoatSmokeTest.json --verbose > /tmp/zlgear_moat.log 2>&1

if grep -q "测试通过 (PASS)" /tmp/zlgear_moat.log; then
    echo "✅ 护城河特性回归通过。"
else
    echo "❌ 护城河特性验证失败！请查看日志 /tmp/zlgear_moat.log"
    # 输出错误摘要
    grep "Error" /tmp/zlgear_moat.log | tail -n 5
    exit 4
fi

echo "===================================================="
echo "🎉 恭喜！发布质量门检查全部通过。该版本可稳定发布。"
echo "===================================================="
exit 0
