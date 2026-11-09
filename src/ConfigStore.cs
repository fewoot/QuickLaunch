using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QuickLaunch
{
    /// <summary>配置读写：%APPDATA%\QuickLaunch\config.json，原子写入 + 自动备份。</summary>
    internal static class ConfigStore
    {
        public static AppConfig Load()
        {
            AppConfig cfg = null;
            try
            {
                if (File.Exists(Helper.ConfigPath))
                {
                    string json = File.ReadAllText(Helper.ConfigPath, Encoding.UTF8);
                    cfg = FromJson(json);
                    if (cfg != null)
                        Logging.Write(LogLevel.INFO, string.Format("配置已载入：{0} 项程序、{1} 个分组（{2}）",
                            cfg.Entries.Count, cfg.Groups.Count, Helper.ConfigPath));
                }
            }
            catch (Exception ex)
            {
                Logging.Write(LogLevel.ERR, "配置读取失败，改用默认配置：" + ex.Message);
                cfg = null;
            }

            if (cfg == null)
            {
                cfg = AppConfig.CreateDefault();
                if (!File.Exists(Helper.ConfigPath))
                    Logging.Write(LogLevel.INFO, "首次运行，已生成默认配置结构（程序列表为空，扫描后添加）。");
            }
            else
            {
                var kept = new List<AppEntry>();
                foreach (AppEntry e in cfg.Entries)
                    if (e != null && Helper.HasText(e.Target)) kept.Add(e);
                cfg.Entries = kept;
                if (cfg.Groups.Count == 0) cfg.Groups = AppConfig.CreateDefault().Groups;
            }
            return cfg;
        }

        public static bool Save(AppConfig cfg, out string error)
        {
            error = "";
            try
            {
                string json = ToJson(cfg);
                string tmp = Helper.ConfigPath + ".tmp";

                if (File.Exists(Helper.ConfigPath))
                {
                    try { File.Copy(Helper.ConfigPath, Helper.ConfigBackupPath, true); }
                    catch { }
                }

                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(Helper.ConfigPath)) File.Delete(Helper.ConfigPath);
                File.Move(tmp, Helper.ConfigPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------ 序列化

        public static string ToJson(AppConfig cfg)
        {
            var root = new Dictionary<string, object>();
            root["version"] = cfg.Version;
            root["defaultDelaySeconds"] = cfg.DefaultDelaySeconds;
            root["serialLaunch"] = cfg.SerialLaunch;
            root["skipRunning"] = cfg.SkipRunning;
            root["foldersInExplorer"] = cfg.FoldersInExplorer;
            root["launchGroupOnStartup"] = cfg.LaunchGroupOnStartup;
            root["startupGroupKey"] = cfg.StartupGroupKey;
            root["minimizeToTray"] = cfg.MinimizeToTray;

            var groups = new List<object>();
            foreach (LaunchGroup g in cfg.Groups)
            {
                var m = new Dictionary<string, object>();
                m["key"] = g.Key;
                m["name"] = g.Name;
                m["icon"] = g.Icon;
                groups.Add(m);
            }
            root["groups"] = groups;

            var entries = new List<object>();
            foreach (AppEntry e in cfg.Entries)
            {
                if (!Helper.HasText(e.Target)) continue;
                var m = new Dictionary<string, object>();
                m["id"] = e.Id;
                m["name"] = e.Name;
                m["target"] = e.Target;
                if (Helper.HasText(e.Arguments)) m["args"] = e.Arguments;
                if (Helper.HasText(e.WorkingDir)) m["cwd"] = e.WorkingDir;
                m["source"] = e.Source;
                m["delay"] = e.DelaySeconds;
                m["group"] = e.Group;
                m["enabled"] = e.Enabled;
                if (e.Favorite) m["favorite"] = true;
                if (e.WindowState != 0) m["window"] = e.WindowState;
                if (Helper.HasText(e.Note)) m["note"] = e.Note;
                // 保留原始 .lnk 路径：启动时优先按"双击快捷方式"处理，能带上 AppUserModelID 等语义
                if (Helper.HasText(e.ShortcutPath)) m["lnk"] = e.ShortcutPath;
                entries.Add(m);
            }
            root["entries"] = entries;

            return Json.Write(root);
        }

        public static AppConfig FromJson(string json)
        {
            object parsed = Json.Parse(json);
            Dictionary<string, object> root = Json.AsMap(parsed);
            if (root == null) return null;

            var cfg = new AppConfig();
            cfg.Version = Json.Int(root, "version", 1);
            cfg.DefaultDelaySeconds = Json.Int(root, "defaultDelaySeconds", 3);
            cfg.SerialLaunch = Json.Bool(root, "serialLaunch", true);
            cfg.SkipRunning = Json.Bool(root, "skipRunning", true);
            cfg.FoldersInExplorer = Json.Bool(root, "foldersInExplorer", true);
            cfg.LaunchGroupOnStartup = Json.Bool(root, "launchGroupOnStartup", false);
            cfg.StartupGroupKey = Json.Str(root, "startupGroupKey", "daily");
            cfg.MinimizeToTray = Json.Bool(root, "minimizeToTray", false);

            cfg.Groups.Clear();
            List<object> groups = Json.AsList(Get(root, "groups"));
            if (groups != null)
            {
                foreach (object o in groups)
                {
                    Dictionary<string, object> m = Json.AsMap(o);
                    if (m == null) continue;
                    string key = Json.Str(m, "key", "");
                    if (!Helper.HasText(key)) continue;
                    cfg.Groups.Add(new LaunchGroup(key, Json.Str(m, "name", key), Json.Str(m, "icon", "●")));
                }
            }

            List<object> entries = Json.AsList(Get(root, "entries"));
            if (entries != null)
            {
                foreach (object o in entries)
                {
                    Dictionary<string, object> m = Json.AsMap(o);
                    if (m == null) continue;
                    var e = new AppEntry();
                    e.Id = (long)Json.Int(m, "id", 0);
                    e.Name = Json.Str(m, "name", "");
                    e.Target = Json.Str(m, "target", "");
                    e.Arguments = Json.Str(m, "args", "");
                    e.WorkingDir = Json.Str(m, "cwd", "");
                    e.Source = Json.Str(m, "source", "手动添加");
                    e.DelaySeconds = Json.Int(m, "delay", cfg.DefaultDelaySeconds);
                    e.Group = Json.Str(m, "group", cfg.Groups.Count > 0 ? cfg.Groups[0].Key : "daily");
                    e.Enabled = Json.Bool(m, "enabled", true);
                    e.Favorite = Json.Bool(m, "favorite", false);
                    e.WindowState = Json.Int(m, "window", 0);
                    e.Note = Json.Str(m, "note", "");
                    e.ShortcutPath = Json.Str(m, "lnk", "");
                    e.IsNew = false;
                    if (Helper.HasText(e.Target)) cfg.Entries.Add(e);
                }
            }

            if (cfg.Groups.Count == 0) cfg.Groups = AppConfig.CreateDefault().Groups;
            return cfg;
        }

        private static object Get(Dictionary<string, object> m, string key)
        {
            object v;
            if (m != null && m.TryGetValue(key, out v)) return v;
            return null;
        }

        /// <summary>给新加的程序分配一个不重复的 Id。</summary>
        public static long NextId(AppConfig cfg)
        {
            long max = 0;
            foreach (AppEntry e in cfg.Entries)
                if (e.Id > max) max = e.Id;
            return max + 1;
        }
    }
}
