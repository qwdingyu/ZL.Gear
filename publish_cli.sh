#!/bin/bash

# ZL.Gear CLI 工具自动发布及打包脚本
# 能够直接提取免受每次编译之苦的独立 CLI 应用程式

set -e

echo "=================================================="
echo "          ZL.Gear CLI 自动发布脚本              "
echo "=================================================="

# 确定脚本执行的根目录
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

CONSOLE_PROJ="../ZL.Gear.Demos/ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj"
PUBLISH_DIR="PublishOutput"

if [ -d "$PUBLISH_DIR" ]; then
    echo "1. 清理旧的发布文件 -> $PUBLISH_DIR"
    rm -rf "$PUBLISH_DIR"
fi

echo "2. 开始编译并发布 CLI 程序 (Release 配置)..."
# 为了减小体积，这里发布为 Framework Dependent (不需要自带运行时的跨平台格式)
# 其中 /p:UseAppHost=true 会在对应操作系统上生成原生的入口可执行文件 (Mac 上的 ZL.Gear.ConsoleApp, 或者是 Windows 的 exe)
dotnet publish "$CONSOLE_PROJ" -c Release -o "$PUBLISH_DIR" /p:UseAppHost=true

echo ""
echo "3. 复制必需的外围资产 (Scenarios / Protocols) 到发布包..."
cp -R "../ZL.Gear.Demos/ZL.Gear.ConsoleApp/Scenarios" "$PUBLISH_DIR/"
cp -R "../ZL.Gear.Demos/ZL.Gear.ConsoleApp/Protocols" "$PUBLISH_DIR/"

echo "4. 设置可执行权限..."
# 在 Mac/Linux 上为了能直接敲应用名运行，需要赋予可执行权限
chmod +x "$PUBLISH_DIR/ZL.Gear.ConsoleApp"

echo "=================================================="
echo "发布成功！✔"
echo "现已打包最新版工具！每次想要测试时，你再也不需要执行 dotnet run 漫长编译了。"
echo "你可以在任何目录通过指定相对/绝对路径来调用 ZL.Gear.ConsoleApp 了："
echo ""
echo "例如："
echo "  >$ROOT_DIR/$PUBLISH_DIR/ZL.Gear.ConsoleApp help"
echo "=================================================="
