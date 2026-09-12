using System;
using System.IO;

namespace ZL.Gear.Testing.Common
{
    /// <summary>
    /// 测试用仓库路径解析（自 TestContext.TestDirectory 向上找 ZL.Gear.sln）。
    /// </summary>
    public static class GearTestPaths
    {
        /// <summary>定位含 ZL.Gear.sln 的仓库根目录。</summary>
        public static string FindRepoRoot(string startDirectory)
        {
            var dir = new DirectoryInfo(startDirectory);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ZL.Gear.sln")))
                {
                    return dir.FullName;
                }
            }

            throw new DirectoryNotFoundException(
                $"无法从 {startDirectory} 向上找到 ZL.Gear.sln（请在仓库内运行测试）。");
        }

        /// <summary>IndustryKit 模板 Handler 源码目录。</summary>
        public static string IndustryKitHandlersDir(string testDirectory) =>
            Path.Combine(
                FindRepoRoot(testDirectory),
                "demos", "IndustryKit", "ZL.Gear.Extension.Station", "Handlers");
    }
}
