using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

namespace QuickLaunch
{
    /// <summary>一条待启动记录。</summary>
    internal sealed class AppEntry : INotifyPropertyChanged
    {
        private long _id;
        private string _name = "";
        private string _target = "";
        private string _arguments = "";
        private string _workingDir = "";
        private string _source = "手动添加";
        private int _delaySeconds = 3;
        private string _group = "daily";
        private bool _enabled = true;
        private bool _favorite;
        private int _windowState;          // 0 正常 1 最大化 2 最小化
        private string _note = "";
        private string _shortcutPath = "";
        private bool _isNew;               // 新加入、还没被勾选过

        public long Id { get { return _id; } set { _id = value; Notify("Id"); } }
        public string Name { get { return _name; } set { _name = value == null ? "" : value; Notify("Name"); } }
        /// <summary>目标：exe 完整路径 / 文件夹 / URL / shell:AppsFolder\... / .lnk 解析结果</summary>
        public string Target { get { return _target; } set { _target = value == null ? "" : value; Notify("Target"); } }
        public string Arguments { get { return _arguments; } set { _arguments = value == null ? "" : value; Notify("Arguments"); } }
        public string WorkingDir { get { return _workingDir; } set { _workingDir = value == null ? "" : value; Notify("WorkingDir"); } }
        public string Source { get { return _source; } set { _source = value == null ? "" : value; Notify("Source"); } }
        public int DelaySeconds { get { return _delaySeconds; } set { _delaySeconds = value < 0 ? 0 : value; Notify("DelaySeconds"); } }
        public string Group { get { return _group; } set { _group = value == null ? "" : value; Notify("Group"); } }
        public bool Enabled { get { return _enabled; } set { _enabled = value; Notify("Enabled"); } }
        public bool Favorite { get { return _favorite; } set { _favorite = value; Notify("Favorite"); } }
        public int WindowState { get { return _windowState; } set { _windowState = value; Notify("WindowState"); } }
        public string Note { get { return _note; } set { _note = value == null ? "" : value; Notify("Note"); } }

        /// <summary>
        /// 如果这条记录是从 .lnk 快捷方式拖进来的，这里保存**原始 .lnk 路径**。
        /// 启动时优先直接 ShellExecute 这个 .lnk —— 这才是"双击快捷方式"的完整语义：
        /// 会带上快捷方式里设置的参数、起始位置、AppUserModelID、以及"以管理员身份运行"标记。
        /// 有些程序（例如 Obsidian）只认这种方式启动。
        /// </summary>
        public string ShortcutPath { get { return _shortcutPath; } set { _shortcutPath = value == null ? "" : value; Notify("ShortcutPath"); } }

        [System.Xml.Serialization.XmlIgnore]
        public bool IsNew { get { return _isNew; } set { _isNew = value; Notify("IsNew"); } }

        public AppEntry() { }

        public AppEntry Clone()
        {
            var c = (AppEntry)MemberwiseClone();
            c._id = Id;
            return c;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string p)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(p));
        }
    }

    /// <summary>一次启动尝试的结果。</summary>
    internal sealed class LaunchResult
    {
        public bool Ok;
        public string Message = "";
        public int ProcessId;
        public long ElapsedMs;
        public bool NeedsAdmin;     // Windows 提示需要提权
        public bool UserCancelled;  // UAC 被取消

        public static LaunchResult Fail(string msg)
        {
            return new LaunchResult { Ok = false, Message = msg };
        }

        public static LaunchResult Good(string msg, int pid, long ms)
        {
            return new LaunchResult { Ok = true, Message = msg, ProcessId = pid, ElapsedMs = ms };
        }
    }

    /// <summary>一个启动分组（底栏一个按钮）。</summary>
    internal sealed class LaunchGroup
    {
        public string Key = "";
        public string Name = "";
        public string Icon = "●";

        public LaunchGroup() { }

        public LaunchGroup(string key, string name, string icon)
        {
            Key = key;
            Name = name;
            Icon = icon;
        }

        public override string ToString() { return Name; }
    }

    /// <summary>全局配置，落到 config.json。</summary>
    internal sealed class AppConfig
    {
        public int Version = 1;
        public List<AppEntry> Entries = new List<AppEntry>();
        public List<LaunchGroup> Groups = new List<LaunchGroup>();
        public int DefaultDelaySeconds = 3;
        public bool SerialLaunch = true;
        public bool SkipRunning = true;
        public bool FoldersInExplorer = true;
        public bool LaunchGroupOnStartup = false;
        public string StartupGroupKey = "daily";
        public bool MinimizeToTray = false;

        public static AppConfig CreateDefault()
        {
            var c = new AppConfig();
            c.Groups.Add(new LaunchGroup("daily", "日常三件套", "★"));
            c.Groups.Add(new LaunchGroup("work", "工作 / 开发", "◆"));
            c.Groups.Add(new LaunchGroup("sys", "系统工具", "⚙"));
            return c;
        }

        public LaunchGroup FindGroup(string key)
        {
            foreach (LaunchGroup g in Groups)
                if (g.Key == key) return g;
            return null;
        }

        public string GroupName(string key)
        {
            LaunchGroup g = FindGroup(key);
            if (g != null) return g.Name;
            return (key == null || key.Length == 0) ? "未分组" : key;
        }
    }

    /// <summary>图标缓存：优先取 exe 真实图标，取不到就退回「彩色方块 + 首字」。</summary>
    internal static class AppIcon
    {
        private static readonly Dictionary<string, Icon> Cache = new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();

        public static Icon Get(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            string key = target;

            lock (Gate)
            {
                if (Cache.ContainsKey(key)) return Cache[key];
            }

            Icon ic = null;
            try
            {
                string path = target;
                if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) path = "";
                if (path.Length > 0 && System.IO.File.Exists(path))
                    ic = Icon.ExtractAssociatedIcon(path);
            }
            catch { ic = null; }

            lock (Gate) { Cache[key] = ic; }
            return ic;
        }

        public static void Clear()
        {
            lock (Gate) { Cache.Clear(); }
        }

        /// <summary>没有真实图标时，按名字生成一个稳定的颜色。</summary>
        public static Color FallbackColor(string name)
        {
            Color[] palette = new Color[]
            {
                Color.FromArgb(66, 133, 244), Color.FromArgb(52, 168, 83), Color.FromArgb(234, 67, 53),
                Color.FromArgb(251, 188, 5), Color.FromArgb(138, 90, 214), Color.FromArgb(0, 150, 160),
                Color.FromArgb(219, 68, 55), Color.FromArgb(255, 112, 67), Color.FromArgb(63, 81, 181),
                Color.FromArgb(0, 121, 107), Color.FromArgb(175, 80, 160), Color.FromArgb(109, 76, 65)
            };
            if (string.IsNullOrEmpty(name)) return palette[0];

            int h = 0;
            foreach (char ch in name) h = (h * 31 + ch) & 0x7fffffff;
            return palette[h % palette.Length];
        }

        public static string Initial(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";

            string n = name.Trim();
            string[] strip = new string[] { "Microsoft ", "Windows ", "Google ", "Adobe ", "腾讯", "网易" };
            foreach (string s in strip)
                if (n.StartsWith(s, StringComparison.OrdinalIgnoreCase) && n.Length > s.Length)
                    n = n.Substring(s.Length);

            char c = n[0];
            return char.IsLetter(c) ? char.ToUpperInvariant(c).ToString() : n.Substring(0, 1);
        }
    }
}
