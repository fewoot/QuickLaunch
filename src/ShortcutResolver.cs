using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace QuickLaunch
{
    /// <summary>
    /// 解析快捷方式 / 网络快捷方式 / shell 目标。没装 Windows Script Host 时自动退回手写 .lnk 解析。
    /// </summary>
    internal static class ShortcutResolver
    {
        public sealed class Target
        {
            public string Path = "";
            public string Arguments = "";
            public string WorkingDir = "";
            public string IconLocation = "";
            public string Description = "";
            public bool IsFolder;

            public bool IsUsable
            {
                get
                {
                    if (!Helper.HasText(Path)) return false;
                    if (Helper.LooksLikeShellTarget(Path)) return true;
                    if (Helper.LooksLikeUrl(Path)) return true;
                    try { return File.Exists(Path) || Directory.Exists(Path); }
                    catch { return false; }
                }
            }
        }

        public static bool ScriptHostAvailable
        {
            get
            {
                try
                {
                    Type t = Type.GetTypeFromProgID("WScript.Shell");
                    return t != null;
                }
                catch { return false; }
            }
        }

        public static Target Resolve(string file)
        {
            if (!Helper.HasText(file)) return null;

            string ext = "";
            try { ext = System.IO.Path.GetExtension(file).ToLowerInvariant(); }
            catch { }

            if (ext == ".url")
            {
                Target u = ResolveUrlFile(file);
                if (u != null) return u;
            }

            // 先用手写解析器：只读文件头几 KB，比 COM 快几十倍
            if (ext == ".lnk")
            {
                Target fast = ResolveLnkBinary(file);
                if (fast != null && fast.IsUsable) return fast;
            }

            Target viaCom = ResolveWithCom(file);
            if (viaCom != null && viaCom.IsUsable) return viaCom;

            if (ext == ".lnk")
            {
                Target slow = ResolveLnkBinary(file);
                if (slow != null && slow.IsUsable) return slow;
            }

            // 兜底：把文件本身当目标（.exe / 文件夹 / 文档 都能直接启动）
            var t = new Target();
            t.Path = file;
            t.WorkingDir = Helper.DirName(file);
            t.IsFolder = Helper.IsFolder(file);
            return t;
        }

        // ------------------------------------------------------------ COM 方式

        private static Target ResolveWithCom(string file)
        {
            object shell = null;
            object link = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return null;
                shell = Activator.CreateInstance(shellType);
                link = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                    null, shell, new object[] { file });

                Type linkType = link.GetType();
                var t = new Target();
                t.Path = Helper.Unquote(GetProp(linkType, link, "TargetPath"));
                t.Arguments = GetProp(linkType, link, "Arguments");
                t.WorkingDir = GetProp(linkType, link, "WorkingDirectory");
                t.IconLocation = Helper.Unquote(GetProp(linkType, link, "IconLocation"));
                t.Description = GetProp(linkType, link, "Description");

                if (!Helper.HasText(t.WorkingDir))
                    t.WorkingDir = Helper.DirName(t.Path);
                try { t.IsFolder = Helper.HasText(t.Path) && Directory.Exists(t.Path); }
                catch { }
                return t;
            }
            catch (Exception ex)
            {
                Logging.Write(LogLevel.WARN, "COM 解析快捷方式失败（" + Helper.ExeName(file) + "）：" + ex.Message);
                return null;
            }
            finally
            {
                Release(link);
                Release(shell);
            }
        }

        private static string GetProp(Type t, object obj, string name)
        {
            try
            {
                object v = t.InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, obj, null);
                return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
            }
            catch { return ""; }
        }

        private static void Release(object comObject)
        {
            try
            {
                if (comObject != null && System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
            }
            catch { }
        }

        // --------------------------------------------------------- 手写 .lnk

        private static Target ResolveLnkBinary(string file)
        {
            FileStream fs = null;
            try
            {
                fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0x4000);
                long flen = fs.Length;
                if (flen < 76) return null;

                int bufLen = (int)Math.Min(flen, 0x4000);   // 头 16KB 足够覆盖 LinkInfo
                byte[] buf = new byte[bufLen];
                int read = fs.Read(buf, 0, bufLen);
                if (read < 76) return null;
                if (BitConverter.ToInt32(buf, 0) != 0x4C) return null;    // 头部大小必须是 0x4C

                uint flags = BitConverter.ToUInt32(buf, 20);
                bool hasIdList = (flags & 0x01) != 0;
                bool hasLinkInfo = (flags & 0x02) != 0;

                int p = 76;
                if (hasIdList)
                {
                    if (p + 2 > read) return null;
                    int idListSize = BitConverter.ToUInt16(buf, p);
                    p += 2 + idListSize;
                }

                var t = new Target();
                string localBase = "";

                if (hasLinkInfo && p + 20 <= read)
                {
                    int liSize = BitConverter.ToInt32(buf, p);
                    int liFlags = BitConverter.ToInt32(buf, p + 4);
                    int localBaseOff = BitConverter.ToInt32(buf, p + 16);
                    bool isUnicode = (liFlags & 0x01) != 0;

                    if (localBaseOff > 0)
                        localBase = ReadString(buf, read, p + localBaseOff, !isUnicode);

                    if (liSize > 0 && p + liSize <= read)
                    {
                        int baseOff = ((liFlags & 0x02) != 0) ? 24 : ((liFlags & 0x04) != 0) ? 20 : 0;
                        if (baseOff > 0 && p + baseOff + 4 <= read)
                        {
                            int relOff = BitConverter.ToInt32(buf, p + baseOff);
                            if (relOff > 0)
                            {
                                string tail = ReadString(buf, read, p + baseOff + relOff, !isUnicode);
                                if (Helper.HasText(tail) && Helper.HasText(localBase))
                                {
                                    try { t.Path = Path.Combine(localBase, tail); }
                                    catch { t.Path = tail; }
                                }
                                else if (Helper.HasText(tail))
                                    t.Path = tail;
                            }
                        }
                        p += liSize;
                    }
                }

                // LinkInfo 缺失或路径无效时，用环境变量数据块里的路径兜底
                if (!SafeExists(t.Path))
                {
                    string envPath = TryReadEnvironmentBlock(buf, read, p);
                    if (Helper.HasText(envPath)) t.Path = envPath;
                }

                t.WorkingDir = Helper.DirName(t.Path);
                try { t.IsFolder = Helper.HasText(t.Path) && Directory.Exists(t.Path); }
                catch { }
                return t;
            }
            catch
            {
                return null;   // 解析失败就交给 COM 兜底，不用刷警告
            }
            finally
            {
                if (fs != null) { try { fs.Dispose(); } catch { } }
            }
        }

        private static bool SafeExists(string p)
        {
            try { return Helper.HasText(p) && (File.Exists(p) || Directory.Exists(p)); }
            catch { return false; }
        }

        /// <summary>环境变量数据块里的「目标路径」（ANSI 版本），用来兼容部分安装器生成的快捷方式。</summary>
        private static string TryReadEnvironmentBlock(byte[] data, int len, int p)
        {
            try
            {
                for (int i = Math.Max(0, p); i + 8 < len; i++)
                {
                    if (data[i] == 0x41 && data[i + 1] == 0x31)   // "A1" 环境变量块
                    {
                        int blockLen = BitConverter.ToInt32(data, i + 4);
                        if (blockLen > 0 && blockLen < 2000 && i + 8 + blockLen <= len)
                        {
                            string s = Encoding.Default.GetString(data, i + 8, blockLen).TrimEnd('\0');
                            int eq = s.IndexOf('=');
                            if (eq > 0 && eq < s.Length - 1)
                            {
                                string candidate = s.Substring(eq + 1).Trim();
                                if (candidate.Length > 2 && (candidate[1] == ':' || candidate.StartsWith("\\")))
                                    return candidate;
                            }
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        private static string ReadString(byte[] data, int len, int offset, bool unicode)
        {
            if (offset <= 0 || offset >= len) return "";
            try
            {
                if (unicode)
                {
                    int end = offset;
                    while (end + 1 < len && !(data[end] == 0 && data[end + 1] == 0)) end += 2;
                    return Encoding.Unicode.GetString(data, offset, end - offset);
                }
                int e2 = offset;
                while (e2 < len && data[e2] != 0) e2++;
                return Encoding.Default.GetString(data, offset, e2 - offset);
            }
            catch { return ""; }
        }

        // ------------------------------------------------------------ .url 文件

        private static Target ResolveUrlFile(string file)
        {
            try
            {
                var t = new Target();
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        t.Path = line.Substring(4).Trim();
                        break;
                    }
                }
                if (Helper.HasText(t.Path)) return t;
            }
            catch { }
            return null;
        }
    }
}
