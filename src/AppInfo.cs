using System.Reflection;
using System.Runtime.InteropServices;

// ---------------------------------------------------------------
// 版本号只在这里改一次，其它地方（标题栏、--version、exe 属性）全部引用
// AppInfo.Version。改完记得同步 src\app.manifest 里的 assemblyIdentity 版本。
// ---------------------------------------------------------------

[assembly: AssemblyTitle("一键启动 QuickLaunch")]
[assembly: AssemblyDescription("Windows 一键启动器：勾选一批程序，按各自的间隔依次打开")]
[assembly: AssemblyProduct("QuickLaunch")]
[assembly: AssemblyCompany("QuickLaunch contributors")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: AssemblyInformationalVersion("1.2.0")]
[assembly: ComVisible(false)]

namespace QuickLaunch
{
    internal static class AppInfo
    {
        /// <summary>程序版本（显示用）。发布时改这里 + app.manifest。</summary>
        public const string Version = "1.2.0";

        /// <summary>英文名。</summary>
        public const string Name = "QuickLaunch";

        /// <summary>中文名。</summary>
        public const string DisplayName = "一键启动";
    }
}
