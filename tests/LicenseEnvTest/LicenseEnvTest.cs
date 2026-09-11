using System;
using System.Collections.Generic;
using ZL.License;

namespace ZL.Gear.Tests;

/// <summary>
/// 授权环境变量行为验证（只读调查 + 运行时真实验证）。
/// 不修改 ZL.License 源码，通过环境变量组合验证当前包版本 2.2.7 的实际行为。
/// </summary>
public static class LicenseEnvTest
{
    /// <summary>
    /// 主验证入口：输出 DevMode 路径在不同构建/环境变量组合下的实际状态。
    /// </summary>
    public static void Run()
    {
        Console.WriteLine("=== ZL.License 环境变量行为验证 ===");

        // 打印关键程序集信息
        var licenseAsm = typeof(LicenseManager).Assembly;
        Console.WriteLine($"ZL.License 程序集: {licenseAsm.GetName().Name}");
        Console.WriteLine($"ZL.License 版本: {licenseAsm.GetName().Version}");
        PrintDefine("RELEASE_BUILD", "ZL.License.csproj Release 构建定义");
        PrintDefine("DEBUG", "系统调试符号");

        // 先直接测试 LicenseConfig.FromEnvironment()
        Console.WriteLine("\n--- 直接测试 LicenseConfig.FromEnvironment() ---");
        
        // 清理所有相关环境变量
        foreach (var key in new[] { "ZL_LICENSE_DEV_MODE", "ZL_LICENSE_DEV_SECRET", "ZL_LICENSE_DEV_TOKEN", "ZL_GEAR_LICENSE_TEST_MODE" })
            Environment.SetEnvironmentVariable(key, null);
        
        // 设置环境变量
        Environment.SetEnvironmentVariable("ZL_LICENSE_DEV_MODE", "true");
        Environment.SetEnvironmentVariable("ZL_LICENSE_DEV_SECRET", "test-secret");
        Environment.SetEnvironmentVariable("ZL_LICENSE_DEV_TOKEN", "test-token");
        
        Console.WriteLine($"  Environment.GetEnvironmentVariable(\"ZL_LICENSE_DEV_MODE\") = {Environment.GetEnvironmentVariable("ZL_LICENSE_DEV_MODE")}");
        Console.WriteLine($"  Environment.GetEnvironmentVariable(\"ZL_LICENSE_DEV_SECRET\") = {Environment.GetEnvironmentVariable("ZL_LICENSE_DEV_SECRET")}");
        Console.WriteLine($"  Environment.GetEnvironmentVariable(\"ZL_LICENSE_DEV_TOKEN\") = {Environment.GetEnvironmentVariable("ZL_LICENSE_DEV_TOKEN")}");
        
        var config = LicenseConfig.FromEnvironment();
        Console.WriteLine($"  Config.DevMode={config.DevMode}");
        Console.WriteLine($"  Config.ProductPrefix={config.ProductPrefix}");

        // 1. 无环境变量基线
        Console.WriteLine("\n--- 1. 无环境变量基线 ---");
        TestScenario(new Dictionary<string, string?>());

        // 2. 仅 DEV_MODE=true
        Console.WriteLine("\n--- 2. 仅 ZL_LICENSE_DEV_MODE=true ---");
        TestScenario(new Dictionary<string, string?>
        {
            ["ZL_LICENSE_DEV_MODE"] = "true"
        });

        // 3. DEV_MODE + SECRET，无 TOKEN
        Console.WriteLine("\n--- 3. ZL_LICENSE_DEV_MODE=true + SECRET，无 TOKEN ---");
        TestScenario(new Dictionary<string, string?>
        {
            ["ZL_LICENSE_DEV_MODE"] = "true",
            ["ZL_LICENSE_DEV_SECRET"] = "test-secret"
        });

        // 4. 完整 DevMode 三件套（DEBUG 构建下应通过；Release 构建下应被强制关闭）
        Console.WriteLine("\n--- 4. 完整 DevMode 三件套 ---");
        TestScenario(new Dictionary<string, string?>
        {
            ["ZL_LICENSE_DEV_MODE"] = "true",
            ["ZL_LICENSE_DEV_SECRET"] = "test-secret",
            ["ZL_LICENSE_DEV_TOKEN"] = ComputeExpectedToken("test-secret")
        });

        // 5. 错误 TOKEN
        Console.WriteLine("\n--- 5. 错误 TOKEN ---");
        TestScenario(new Dictionary<string, string?>
        {
            ["ZL_LICENSE_DEV_MODE"] = "true",
            ["ZL_LICENSE_DEV_SECRET"] = "test-secret",
            ["ZL_LICENSE_DEV_TOKEN"] = "BADTOKEN"
        });

        // 6. 产品前缀变体（ZL.Gear 通常使用默认前缀 ZL_LICENSE_）
        Console.WriteLine("\n--- 6. 默认前缀 ZL_LICENSE_DEV_MODE=true ---");
        TestScenario(new Dictionary<string, string?>
        {
            ["ZL_LICENSE_DEV_MODE"] = "true"
        });

        Console.WriteLine("\n=== 验证完成 ===");
    }

    private static void TestScenario(Dictionary<string, string?> env)
    {
        // 重置 LicenseManager 状态，确保每次测试独立
        LicenseManager.Reset();

        // 清理并设置环境变量
        foreach (var key in new[] { "ZL_LICENSE_DEV_MODE", "ZL_LICENSE_DEV_SECRET", "ZL_LICENSE_DEV_TOKEN", "ZL_GEAR_LICENSE_TEST_MODE" })
            Environment.SetEnvironmentVariable(key, null);

        foreach (var kv in env)
            Environment.SetEnvironmentVariable(kv.Key, kv.Value);

        // 验证环境变量确实被设置
        var devModeEnv = Environment.GetEnvironmentVariable("ZL_LICENSE_DEV_MODE");
        var secretEnv = Environment.GetEnvironmentVariable("ZL_LICENSE_DEV_SECRET");
        var tokenEnv = Environment.GetEnvironmentVariable("ZL_LICENSE_DEV_TOKEN");
        Console.WriteLine($"  [环境变量] ZL_LICENSE_DEV_MODE={devModeEnv}, SECRET={secretEnv}, TOKEN={tokenEnv}");

        // 直接调用 LicenseConfig.FromEnvironment() 验证
        var cfg = LicenseConfig.FromEnvironment();
        Console.WriteLine($"  [LicenseConfig] DevMode={cfg.DevMode}");

        try
        {
            // 每次重新初始化以读取环境变量
            LicenseManager.Initialize();
            
            // 通过反射读取内部状态用于诊断
            var configField = typeof(LicenseManager).GetField("_lastConfig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var devModeField = typeof(LicenseManager).GetField("_devMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var stateField = typeof(LicenseManager).GetField("_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            var config = configField?.GetValue(null);
            var devMode = devModeField != null ? (bool)devModeField.GetValue(null) : false;
            var state = stateField != null ? (LicenseState)stateField.GetValue(null) : LicenseState.Uninitialized;
            
            // 读取 LicenseConfig 内部值
            string? devModeFromConfig = null;
            if (config != null)
            {
                var devModeProp = config.GetType().GetProperty("DevMode");
                if (devModeProp != null)
                    devModeFromConfig = devModeProp.GetValue(config)?.ToString();
            }

            var publicState = LicenseManager.State;
            var auth = LicenseManager.IsAuthorized;
            var trial = LicenseManager.IsTrial;
            var allowedBasic = LicenseManager.AllowOperation("basic");

            Console.WriteLine($"  State={publicState}, Authorized={auth}, Trial={trial}, AllowOperation(basic)={allowedBasic}");
            Console.WriteLine($"  [诊断] _devMode={devMode}, _state={state}, Config.DevMode={devModeFromConfig ?? "null"}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string ComputeExpectedToken(string secret)
    {
        // 与 MachineFingerprint.GetCurrent() + SHA256(指纹+密钥) 一致
        // 注意：这里需要运行时真实指纹，无法在编译期准确计算。
        // 因此仅用于展示“正确令牌格式”，不保证通过校验。
        var fp = MachineFingerprint.GetCurrent();
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fp + secret));
        return BitConverter.ToString(hash).Replace("-", "");
    }

    private static void PrintDefine(string define, string desc)
    {
        var hasDefine = false;
        try
        {
            hasDefine = define switch
            {
                "RELEASE_BUILD" => IsReleaseBuild(),
                "DEBUG" => IsDebugBuild(),
                _ => false
            };
        }
        catch { }

        Console.WriteLine($"{define}: {(hasDefine ? "已定义" : "未定义")} ({desc})");
    }

    private static bool IsReleaseBuild()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
