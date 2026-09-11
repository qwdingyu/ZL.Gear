using System;
using ZL.License;

namespace ZL.Gear.Engine;

/// <summary>
/// 授权门禁（进程级单次检查）。
/// </summary>
internal static class LicenseGuard
{
    /// <summary>
    /// 测试模式开关：仅用于单测绕过 LicenseManager 授权检查，避免测试环境受 DevMode/证书/TrialStamp 影响。
    /// 生产代码不会设置此环境变量，因此不会影响线上授权门禁。
    /// </summary>
    private static bool IsTestMode =>
        string.Equals(Environment.GetEnvironmentVariable("ZL_GEAR_LICENSE_TEST_MODE"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 确保进程已通过授权初始化（仅执行一次）。
    /// </summary>
    public static void EnsureAuthorized()
    {
        if (IsTestMode) return;

        // ZL.License 进程级单例，多次调用不会重复初始化
        LicenseManager.Initialize();

        if (!LicenseManager.IsAuthorized && !LicenseManager.IsTrial)
        {
            // 未授权且不在 Trial：直接拒绝
            throw new LicenseException(
                "ZL.Gear 未检测到有效授权证书或试用许可。" +
                "请配置 ZL_LICENSE_CERT 环境变量或联系供应商获取商业授权。");
        }
    }

    /// <summary>
    /// 确保当前授权状态允许执行操作。
    /// </summary>
    /// <param name="feature">功能掩码（如 "basic"、"siemens-s7"），连接层传 null。</param>
    public static void EnsureOperationAllowed(string? feature = null)
    {
        if (IsTestMode) return;

        EnsureAuthorized();

        if (!LicenseManager.AllowOperation(feature))
        {
            throw new LicenseException($"当前授权状态不允许执行该操作（feature={feature}）。");
        }
    }
}
