using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QuickLaunch
{
    /// <summary>各种小工具函数，避免每个文件重复实现。</summary>
    internal static class Helper
    {
        // ---------------------------------------------------------- 字符串整形

        /// <summary>去掉首尾空白和成对引号。</summary>
        public static string Unquote(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        public static bool HasText(string s)
        {
            return !string.IsNullOrEmpty(s) && s.Trim().Length > 0;
        }

        /// <summary>路径比较用（大小写不敏感）。</summary>
        public static bool SamePath(string a, string b)
        {
            if (!HasText(a) || !HasText(b)) return false;
            return string.Equals(a.Trim().TrimEnd('\\'), b.Trim().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把命令串拆成 可执行文件 + 参数（处理 "C:\a b\x.exe" /a 这种）。</summary>
        public static void SplitCommand(string command, out string file, out string args)
        {
            file = ""; args = "";
            if (!HasText(command)) return;
            string c = command.Trim();

            if (c[0] == '"')
            {
                int end = c.IndexOf('"', 1);
                if (end > 0)
                {
                    file = c.Substring(1, end - 1);
                    args = c.Substring(end + 1).Trim();
                    return;
                }
            }

            int comma = c.IndexOf(',');
            if (comma > 0 && c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) > comma)
                c = c.Substring(0, comma);   // 形如  C:\x.exe,0

            int sp = c.IndexOf(' ');
            if (sp > 0)
            {
                string head = c.Substring(0, sp);
                if (head.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    file = head;
                    args = c.Substring(sp + 1).Trim();
                    return;
                }
            }
            file = c;
        }

        /// <summary>把 DisplayIcon 这种值解析成 exe 路径："C:\a\x.exe",0 或 C:\a\x.exe -icon</summary>
        public static string ParseDisplayIcon(string raw)
        {
            if (!HasText(raw)) return "";
            string s = raw.Trim();

            if (s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end > 0)
                    return CleanIconPath(s.Substring(1, end - 1));
            }

            // 先剥掉结尾的 ,0 / ,-12 这类图标索引
            int comma = s.LastIndexOf(',');
            if (comma > 0)
            {
                string tail = s.Substring(comma + 1).Trim();
                int dummy;
                if (tail.Length > 0 && int.TryParse(tail, out dummy))
                    s = s.Substring(0, comma).Trim();
            }

            // 再砍掉 -icon / /icon 这类参数
            int cut = s.IndexOf(" -", StringComparison.Ordinal);
            if (cut > 0) s = s.Substring(0, cut);
            cut = s.IndexOf(" /", StringComparison.Ordinal);
            if (cut > 0) s = s.Substring(0, cut);

            return CleanIconPath(s);
        }

        private static string CleanIconPath(string s)
        {
            s = Unquote(s);
            if (s.IndexOf('%') >= 0)
            {
                try { s = Environment.ExpandEnvironmentVariables(s); }
                catch { }
            }
            return s;
        }

        /// <summary>从形如 "C:\a\b.exe --arg" 里抽出 exe 路径。</summary>
        public static string ExtractExePath(string cmd)
        {
            string file, args;
            SplitCommand(cmd, out file, out args);
            return Unquote(file);
        }

        public static string ExeName(string path)
        {
            if (!HasText(path)) return "";
            try { return Path.GetFileName(path); }
            catch { return path; }
        }

        public static string DirName(string path)
        {
            if (!HasText(path)) return "";
            try { return Path.GetDirectoryName(path); }
            catch { return ""; }
        }

        /// <summary>程序名归一化，用于「文件名像不像这个程序」的打分。</summary>
        public static string Normalize(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // --------------------------------------------------------------- 路径

        public static string AppDir
        {
            get
            {
                try
                {
                    string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    if (HasText(dir)) return dir;
                }
                catch { }
                return Environment.CurrentDirectory;
            }
        }

        /// <summary>外部指定的数据目录（便携模式 / 自检模式用，--cfgdir=路径）。</summary>
        public static string DataDirOverride;

        /// <summary>外部指定的日志目录（自检模式用，--logdir=路径）。</summary>
        public static string LogDirOverride;

        public static string DataDir
        {
            get
            {
                if (HasText(DataDirOverride))
                {
                    try { Directory.CreateDirectory(DataDirOverride); } catch { }
                    return DataDirOverride;
                }

                // 环境变量兜底：测试程序（LayoutTest / Shot）自己没法调 Main 里的参数解析，
                // 但它会触发"退出自动保存"。设了这个变量，整个进程就只认隔离目录，
                // 绝不会碰用户真实的 config.json。
                string envCfg = null;
                try { envCfg = Environment.GetEnvironmentVariable("QL_CFGDIR"); }
                catch { }
                if (HasText(envCfg))
                {
                    try { Directory.CreateDirectory(envCfg); } catch { }
                    return envCfg;
                }

                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "QuickLaunch");
                try { Directory.CreateDirectory(dir); }
                catch { }

                if (IsWritable(dir)) return dir;

                // AppData 写不了就退回 exe 同级目录，保证配置一定有地方放
                string beside = Path.Combine(AppDir, "config");
                try { Directory.CreateDirectory(beside); } catch { }
                if (IsWritable(beside)) return beside;

                return dir;
            }
        }

        /// <summary>探测目录是否真的可写（创建失败或只读都会返回 false）。</summary>
        public static bool IsWritable(string dir)
        {
            if (!HasText(dir)) return false;
            try
            {
                if (!Directory.Exists(dir)) return false;
                string probe = Path.Combine(dir, ".writetest_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                File.WriteAllText(probe, "ok", Encoding.UTF8);
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static string ConfigPath { get { return Path.Combine(DataDir, "config.json"); } }
        public static string ConfigBackupPath { get { return Path.Combine(DataDir, "config.backup.json"); } }

        /// <summary>exe 同级 logs 目录；不可写时退回 %APPDATA%\QuickLaunch\logs。</summary>
        public static string LogDir
        {
            get
            {
                if (HasText(LogDirOverride))
                {
                    try { Directory.CreateDirectory(LogDirOverride); } catch { }
                    return LogDirOverride;
                }

                string local = Path.Combine(AppDir, "logs");
                try { Directory.CreateDirectory(local); } catch { }
                if (IsWritable(local)) return local;

                string roam = Path.Combine(DataDir, "logs");
                try { Directory.CreateDirectory(roam); } catch { }
                return roam;
            }
        }

        /// <summary>定位到目标所在文件夹（exe 用所在目录，文件用所在目录，其它空）。</summary>
        public static string TargetFolder(string target)
        {
            if (!HasText(target)) return "";
            if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return "";
            if (target.IndexOf("://", StringComparison.Ordinal) > 0) return "";
            try
            {
                if (Directory.Exists(target)) return target;
                string dir = Path.GetDirectoryName(target);
                if (HasText(dir) && Directory.Exists(dir)) return dir;
            }
            catch { }
            return "";
        }

        public static bool LooksLikeUrl(string s)
        {
            if (!HasText(s)) return false;
            return s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
        }

        public static bool LooksLikeShellTarget(string s)
        {
            if (!HasText(s)) return false;
            return s.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("::", StringComparison.Ordinal)
                || s.StartsWith("{");   // GUID 形式的 shell 命名空间
        }

        public static bool IsFolder(string s)
        {
            try { return HasText(s) && Directory.Exists(s); }
            catch { return false; }
        }

        public static bool FileExistsSafe(string s)
        {
            try { return HasText(s) && File.Exists(s); }
            catch { return false; }
        }

        /// <summary>逗号分隔的字符串列表 ↔ List&lt;string&gt;。</summary>
        public static List<string> SplitList(string s)
        {
            var list = new List<string>();
            if (!HasText(s)) return list;
            foreach (string p in s.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                list.Add(p.Trim());
            return list;
        }

        public static string JoinList(IEnumerable<string> items)
        {
            var sb = new StringBuilder();
            foreach (string s in items)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(s);
            }
            return sb.ToString();
        }

        /// <summary>把秒数说成人话：95 → 1 分 35 秒。</summary>
        public static string HumanSeconds(int total)
        {
            if (total < 60) return total + " 秒";
            int m = total / 60, s = total % 60;
            return s == 0 ? m + " 分钟" : m + " 分 " + s + " 秒";
        }
    }
}
