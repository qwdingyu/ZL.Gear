#!/bin/bash

# ZL.Gear 发布质量门 (Quality Gate)
# 用途: 在发布新版本前，自动验证所有核心逻辑和经典场景。

echo "===================================================="
echo "🚀 ZL.Gear 自动化发布质量门检查启动..."
echo "===================================================="
TOTAL_STEPS=9

# 1. 编译自检
echo "[1/${TOTAL_STEPS}] 正在进行全项目编译自检..."
dotnet build ZL.Gear.sln -v q
if [ $? -ne 0 ]; then
    echo "❌ 编译失败！请检查代码错误。"
    exit 1
fi
echo "✅ 编译通过。"

# 2. 核心单元测试
echo "[2/${TOTAL_STEPS}] 正在运行单元测试..."
dotnet test ZL.Gear.sln --no-build -v q
if [ $? -ne 0 ]; then
    echo "❌ 单元测试末通过！请检查逻辑回归。"
    exit 2
fi
echo "✅ 单元测试全部通过。"

# 3. 表达式方言门禁（docs/133）
echo "[3/${TOTAL_STEPS}] 正在运行 ExprDialectProof（须 PROOF_ALL_PASS）..."
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
echo "[4/${TOTAL_STEPS}] 正在运行经典集成场景 (Mock 模式)..."
export ZL_GEAR_FORCE_MOCK=true
dotnet build ./ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj -c Release -v q
if [ $? -ne 0 ]; then
    echo "❌ ConsoleApp 编译失败！"
    exit 3
fi
# 运行完整集成测试场景（--no-build 避免重复编译，见 docs/141 §10.3）
dotnet run --project ./ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj -c Release --no-build -- -s ./ZL.Gear.ConsoleApp/Scenarios/FullIntegrationTest.json --verbose > /tmp/zlgear_test.log 2>&1

if grep -q "测试通过 (PASS)" /tmp/zlgear_test.log; then
    echo "✅ 经典场景回归通过。"
else
    echo "❌ 场景执行异常！请查看日志 /tmp/zlgear_test.log"
    exit 3
fi

# 5. 关键特性验证 (护城河特性自检)
echo "[5/${TOTAL_STEPS}] 正在验证护城河特性 (资源锁、重试、逻辑分支)..."
dotnet run --project ./ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj -c Release --no-build -- -s ./ZL.Gear.ConsoleApp/Scenarios/MoatSmokeTest.json --verbose > /tmp/zlgear_moat.log 2>&1

if grep -q "测试通过 (PASS)" /tmp/zlgear_moat.log; then
    echo "✅ 护城河特性回归通过。"
else
    echo "❌ 护城河特性验证失败！请查看日志 /tmp/zlgear_moat.log"
    # 输出错误摘要
    grep "Error" /tmp/zlgear_moat.log | tail -n 5
    exit 4
fi

# 6. 行业模板客户端闭环（docs/139）
echo "[6/${TOTAL_STEPS}] 正在运行 IndustryKit verify（须 INDUSTRY_KIT_VERIFY_PASS）..."
dotnet build ./samples/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj -c Release -v q
if [ $? -ne 0 ]; then
    echo "❌ IndustryKit 编译失败！"
    exit 6
fi
KIT_OUT=$(dotnet run --project ./samples/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj -c Release --no-build -- verify 2>&1)
echo "$KIT_OUT" | tail -n 8
if echo "$KIT_OUT" | grep -q "INDUSTRY_KIT_VERIFY_PASS"; then
    echo "✅ IndustryKit 闭环通过。"
else
    echo "❌ IndustryKit 未输出 INDUSTRY_KIT_VERIFY_PASS。"
    exit 7
fi

# 7. Extensions.Data 回归（docs/141 G-04；Tests 已在 sln，本步为 Release 专项 + SQLite 包还原）
echo "[7/${TOTAL_STEPS}] 正在运行 Extensions.Data.Tests..."
dotnet test ./ZL.Gear.Extensions.Data.Tests/ZL.Gear.Extensions.Data.Tests.csproj -c Release -v q
if [ $? -ne 0 ]; then
    echo "❌ Extensions.Data.Tests 未通过！"
    exit 8
fi
echo "✅ Extensions.Data.Tests 通过。"

# 8. Sync 信令场景回归（docs/141 G-03；Mock 模式；信令对依赖测试树 DependsOn 预注册）
echo "[8/${TOTAL_STEPS}] 正在运行 Sync 信令场景 (Demo_Sync_SignalPair)..."
dotnet run --project ./ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj -c Release --no-build -- -s ./ZL.Gear.ConsoleApp/Scenarios/Demo_Sync_SignalPair.json --verbose > /tmp/zlgear_sync.log 2>&1

if grep -q "测试通过 (PASS)" /tmp/zlgear_sync.log; then
    echo "✅ Sync 信令场景通过。"
else
    echo "❌ Sync 信令场景失败！请查看日志 /tmp/zlgear_sync.log"
    exit 9
fi

# 9. Continuous 采样橱窗回归（docs/141 G-02；Mock 模式）
echo "[9/${TOTAL_STEPS}] 正在运行 Continuous 采样橱窗 (Demo_Sampling_Continuous)..."
dotnet run --project ./ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj -c Release --no-build -- -s ./ZL.Gear.ConsoleApp/Scenarios/Demo_Sampling_Continuous.json --verbose > /tmp/zlgear_continuous.log 2>&1

if grep -q "测试通过 (PASS)" /tmp/zlgear_continuous.log; then
    echo "✅ Continuous 采样橱窗通过。"
else
    echo "❌ Continuous 采样橱窗失败！请查看日志 /tmp/zlgear_continuous.log"
    exit 10
fi

echo "===================================================="
echo "🎉 恭喜！发布质量门检查全部通过。该版本可稳定发布。"
echo "===================================================="
exit 0
