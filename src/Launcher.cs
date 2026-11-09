using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickLaunch
{
    /// <summary>
    /// 启动引擎。用 Win32 CreateProcess 而不是 Process.Start，
    /// 因为要拿到「需要管理员权限」这个具体错误码，好给出人话提示。
    /// </summary>
    internal static class Launcher
    {
        // ---------------------------------------------------------- P/Invoke

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public string lpVerb;
            public string lpFile;
            public string lpParameters;
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(
            string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ShellExecuteEx(SHELLEXECUTEINFO info);

        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
        private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
        private const int ERROR_ELEVATION_REQUIRED = 740;
        private const int ERROR_CANCELLED = 1223;

        // ------------------------------------------------------------- 判断

        private static bool IsBatch(string path)
        {
            string e = "";
            try { e = Path.GetExtension(path).ToLowerInvariant(); }
            catch { }
            return e == ".bat" || e == ".cmd";
        }

        private static bool IsExe(string path)
        {
            string e = "";
            try { e = Path.GetExtension(path).ToLowerInvariant(); }
            catch { }
            return e == ".exe" || e == ".com";
        }

        /// <summary>
        /// 该目标是否已在运行。
        ///
        /// 两级判定，缺一不可：
        ///   1. 精确比对进程主模块的完整路径（最准，但读 MainModule 可能因权限失败）；
        ///   2. 退回比对进程名（不含扩展名）—— 很多程序最小化到托盘后，
        ///      读 MainModule 会抛权限异常，只靠第 1 级会漏判，
        ///      于是以为"没在运行"就去启动，结果被单实例机制转发给托盘里的那个实例，
        ///      新进程秒退、窗口不出现 —— 用户看到的就是"点了打不开"。
        /// </summary>
        public static bool IsRunning(string target)
        {
            if (!Helper.HasText(target) || !IsExe(target)) return false;

            string wantPath = "";
            string wantName = "";
            try
            {
                wantPath = Path.GetFullPath(target);
                wantName = Path.GetFileNameWithoutExtension(target);
            }
            catch { return false; }
            if (wantName.Length == 0) return false;

            bool sawSameName = false;

            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return false; }

            foreach (Process p in all)
            {
                try
                {
                    // ---- 第 1 级：完整路径精确匹配
                    try
                    {
                        if (p.MainModule != null && p.MainModule.FileName != null
                            && string.Equals(p.MainModule.FileName, wantPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch { }

                    // ---- 第 2 级：进程名兜底（读不到 MainModule 时也能命中）
                    string name = null;
                    try { name = p.ProcessName; } catch { }
                    if (name != null && string.Equals(name, wantName, StringComparison.OrdinalIgnoreCase))
                        sawSameName = true;
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            return sawSameName;
        }

        private static readonly Dictionary<string, bool> AdminCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly object AdminGate = new object();

        /// <summary>
        /// 静态探测某个 exe 是否需要管理员权限：解析 PE 里内嵌的清单。
        /// 只读文件头 1MB，绝不用 ReadAllBytes（否则碰上几百 MB 的程序会卡死），也绝不运行它。
        /// </summary>
        public static bool RequiresAdmin(string exePath)
        {
            try
            {
                if (!Helper.FileExistsSafe(exePath)) return false;
                string key = Path.GetFullPath(exePath);

                lock (AdminGate)
                {
                    bool cached;
                    if (AdminCache.TryGetValue(key, out cached)) return cached;
                }

                bool result = false;
                string ext = Path.GetExtension(key).ToLowerInvariant();
                if (ext == ".exe" || ext == ".com")
                {
                    byte[] head = ReadHead(key, 0x100000);
                    if (head != null)
                    {
                        string manifest = ExtractManifest(head);
                        if (Helper.HasText(manifest))
                            result = manifest.IndexOf("requireAdministrator", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }

                lock (AdminGate) { AdminCache[key] = result; }
                return result;
            }
            catch { return false; }
        }

        private static byte[] ReadHead(string path, int max)
        {
            FileStream fs = null;
            try
            {
                fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 0x4000);
                long len = fs.Length;
                int want = (int)Math.Min(len, max);
                byte[] buf = new byte[want];
                int got = fs.Read(buf, 0, want);
                if (got != want) Array.Resize(ref buf, got);
                return buf;
            }
            catch { return null; }
            finally { if (fs != null) { try { fs.Dispose(); } catch { } } }
        }

        private static string ExtractManifest(byte[] d)
        {
            if (d == null || d.Length < 0x100) return "";
            if (d[0] != 'M' || d[1] != 'Z') return "";

            int pe = BitConverter.ToInt32(d, 0x3C);
            if (pe <= 0 || pe + 24 >= d.Length) return "";
            if (BitConverter.ToInt32(d, pe) != 0x00004550) return "";   // "PE\0\0"

            int coff = pe + 4;
            int numSections = BitConverter.ToUInt16(d, coff + 2);
            int optSize = BitConverter.ToUInt16(d, coff + 16);
            int opt = coff + 20;
            if (opt + 2 >= d.Length) return "";

            ushort magic = BitConverter.ToUInt16(d, opt);
            int ddOff = (magic == 0x20b) ? opt + 112 : opt + 96;         // PE32+ / PE32 数据目录
            int secOff = opt + optSize;

            for (int i = 0; i < numSections; i++)
            {
                int s = secOff + i * 40;
                if (s + 40 > d.Length) break;
                string name = System.Text.Encoding.ASCII.GetString(d, s, 8).TrimEnd('\0');

                if (name == ".rsrc")
                {
                    int rsrcRva = BitConverter.ToInt32(d, s + 12);
                    int rsrcPtr = BitConverter.ToInt32(d, s + 20);
                    string m = FindManifestInRsrc(d, rsrcPtr, rsrcRva, ddOff);
                    if (Helper.HasText(m)) return m;
                }

                if (name == ".crt" || name == ".CRT")
                {
                    // 极少数程序把清单放在 .crt 段，这里靠资源目录找不到时不做额外处理
                }
            }
            return "";
        }

        /// <summary>在 .rsrc 段里按资源目录树找 RT_MANIFEST(24) → 24/1/2052 这类路径。</summary>
        private static string FindManifestInRsrc(byte[] d, int rsrcPtr, int rsrcRva, int ddOff)
        {
            try
            {
                int resDirRva = BitConverter.ToInt32(d, ddOff + 2 * 8);   // 数据目录第 3 项 = 资源表
                int resDirSize = BitConverter.ToInt32(d, ddOff + 2 * 8 + 4);
                if (resDirRva <= 0 || resDirSize <= 0) return "";

                int baseOff = rsrcPtr - rsrcRva;    // 段内偏移 = RVA + baseOff
                int root = resDirRva + baseOff;
                if (root < 0 || root + 16 > d.Length) return "";

                // 第一层：找类型 24 (RT_MANIFEST)
                int typeEntry = FindDirEntry(d, root, 24);
                if (typeEntry < 0) return "";
                int nameEntry = FindDirEntry(d, typeEntry, -1);          // 第二层：名字（通常 1）
                if (nameEntry < 0) return "";

                // 第三层：语言，取第一个
                int langDir = nameEntry;
                int count = BitConverter.ToUInt16(d, langDir + 12);
                int firstEntry = langDir + 16;
                if (count <= 0 || firstEntry + 8 > d.Length) return "";

                int dataRva = BitConverter.ToInt32(d, firstEntry + 4);
                int dataOff = dataRva + baseOff;
                if (dataOff < 0 || dataOff + 16 > d.Length) return "";

                int dataSize = BitConverter.ToInt32(d, dataOff + 4);
                int dataPtr = BitConverter.ToInt32(d, dataOff + 8) + baseOff;
                if (dataSize <= 0 || dataPtr < 0 || dataPtr + dataSize > d.Length) return "";

                byte[] raw = new byte[dataSize];
                Array.Copy(d, dataPtr, raw, 0, dataSize);
                string xml = System.Text.Encoding.UTF8.GetString(raw);
                if (xml.IndexOf("assembly", StringComparison.OrdinalIgnoreCase) >= 0) return xml;
            }
            catch { }
            return "";
        }

        /// <summary>在资源目录里按 Id 找子目录；idOrFirst 传 -1 表示「取第一个」。</summary>
        private static int FindDirEntry(byte[] d, int dirOff, int idOrFirst)
        {
            if (dirOff < 0 || dirOff + 16 > d.Length) return -1;
            int named = BitConverter.ToUInt16(d, dirOff + 12);
            int ids = BitConverter.ToUInt16(d, dirOff + 14);
            int total = named + ids;
            if (total <= 0) return -1;

            int p = dirOff + 16;
            for (int i = 0; i < total; i++)
            {
                if (p + 8 > d.Length) return -1;
                int nameId = BitConverter.ToInt32(d, p);
                int off = BitConverter.ToInt32(d, p + 4);
                p += 8;

                bool isDir = (off & 0x80000000) != 0;
                int child = off & 0x7FFFFFFF;

                if (idOrFirst < 0 || (i >= named && nameId == idOrFirst))
                {
                    if (!isDir) return -1;
                    return dirOff + child;
                }
            }
            return -1;
        }

        // --------------------------------------------------------------- 启动

        public static LaunchResult Launch(AppEntry e, bool foldersInExplorer)
        {
            var sw = Stopwatch.StartNew();
            if (e == null || !Helper.HasText(e.Target))
                return LaunchResult.Fail("目标为空");

            string target = e.Target.Trim();
            string args = e.Arguments == null ? "" : e.Arguments.Trim();
            string cwd = e.WorkingDir == null ? "" : e.WorkingDir.Trim();
            int window = e.WindowState;

            LaunchResult r;

            // ---- 优先：直接 ShellExecute 原始的 .lnk 快捷方式
            //
            // 为什么不直接启动解析出来的 exe：.lnk 里除了目标路径，还可能带着
            // 启动参数、起始位置、AppUserModelID、"以管理员身份运行"等标记。
            // 直接看 exe 会把这些统统丢掉。而"双击快捷方式"才是用户认知里的”打开"。
            //
            // 真实案例：Obsidian 用 exe 路径启动会立刻退出（退出码 0，连它自己的日志都不写），
            // 但用资源管理器双击快捷方式就能打开 —— 差别就在这层快捷方式语义。
            if (Helper.HasText(e.ShortcutPath) && Helper.FileExistsSafe(e.ShortcutPath)
                && !Helper.LooksLikeShellTarget(target))
            {
                if (Helper.IsFolder(target))
                {
                    // 指向文件夹的快捷方式：交给 shell 打开文件夹即可
                    r = foldersInExplorer || !Directory.Exists(target)
                        ? OpenInExplorer(target)
                        : Direct(target, args, cwd, window);
                }
                else
                {
                    Logging.Info("  通过快捷方式启动（保留 .lnk 里的参数/起始位置/AppUserModelID）");
                    // lpDirectory 传空：让 shell 按快捷方式里自己的"起始位置"来，最贴近双击
                    r = ShellExecute("open", e.ShortcutPath, "", "", window);
                    r.ElapsedMs = sw.ElapsedMilliseconds;
                    return r;
                }

                r.ElapsedMs = sw.ElapsedMilliseconds;
                return r;
            }

            if (Helper.LooksLikeShellTarget(target) || Helper.LooksLikeUrl(target))
                r = ShellOnly(target, args, cwd, window);

            else if (Helper.IsFolder(target))
                r = foldersInExplorer || !Directory.Exists(target)
                    ? OpenInExplorer(target)
                    : Direct(target, args, cwd, window);

            else if (!Helper.FileExistsSafe(target))
                return LaunchResult.Fail("目标不存在：可能已被卸载、改名或所在磁盘没插上。");

            else if (IsBatch(target))
                r = Direct(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                           "/c \"" + target + "\" " + args, Helper.DirName(target), window);

            else if (IsExe(target))
                r = Direct(target, args, cwd, window);

            else
                r = ShellOnly(target, args, cwd, window);   // 文档类目标交给系统关联程序

            r.ElapsedMs = sw.ElapsedMilliseconds;
            return r;
        }

        /// <summary>
        /// 启动后秒退时，是否自动改用 ShellExecute 重试。
        /// 有些程序（Electron 系、单实例应用）用 CreateProcess 拉起后会立刻退出，
        /// 需要经过 shell 激活才能真正弹出窗口 —— 这时重试一次就能开。
        /// </summary>
        public static bool AutoRetryIfExited = true;

        /// <summary>CreateProcess 直启。</summary>
        private static LaunchResult Direct(string exe, string args, string cwd, int window)
        {
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            si.dwFlags = 1;   // STARTF_USESHOWWINDOW
            si.wShowWindow = (short)ShowFlag(window);

            string cmdLine = "\"" + exe + "\"";
            if (Helper.HasText(args)) cmdLine += " " + args;

            if (!Helper.HasText(cwd) || !SafeDirExists(cwd)) cwd = Helper.DirName(exe);

            PROCESS_INFORMATION pi;
            bool ok = CreateProcess(exe, cmdLine, IntPtr.Zero, IntPtr.Zero, false,
                CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_PROCESS_GROUP,
                IntPtr.Zero, Helper.HasText(cwd) ? cwd : null, ref si, out pi);

            if (ok)
            {
                int pid = pi.dwProcessId;
                try { CloseHandle(pi.hThread); } catch { }
                try { CloseHandle(pi.hProcess); } catch { }

                if (AutoRetryIfExited) ScheduleLivenessCheck(exe, args, cwd, window, pid);
                return LaunchResult.Good("已启动", pid, 0);
            }

            int err = Marshal.GetLastWin32Error();

            if (err == ERROR_ELEVATION_REQUIRED)
            {
                Logging.Write(LogLevel.WARN, "  ↑ 该程序要求管理员权限，正在弹出 UAC 授权框，请在弹出的窗口里点「是」");
                LaunchResult r = ShellExecute("runas", exe, args, cwd, window);
                r.NeedsAdmin = true;
                return r;
            }

            string hint = new System.ComponentModel.Win32Exception(err).Message;
            Logging.Write(LogLevel.WARN, string.Format("  CreateProcess 失败（错误码 {0}：{1}），改用 ShellExecute 重试", err, hint));

            LaunchResult sr = ShellExecute("open", exe, args, cwd, window);
            if (!sr.Ok) sr.Message = sr.Message + "（原生启动失败：" + hint + "）";
            return sr;
        }

        /// <summary>
        /// 启动后盯 1.2 秒：如果进程已经退出，把退出码记进日志；
        /// 退出码是错误（非 0）时自动改用 ShellExecute 重试一次。
        ///
        /// 为什么需要它：CreateProcess 成功只代表"进程创建成功"，
        /// 程序可能因为缺运行库 / 权限不足 / 需要 shell 激活而立刻退出。
        /// 之前这种情况界面上还显示"已启动 PID xxx"，其实窗口根本没出来 —— 用户看到的就是"点不开"。
        /// </summary>
        private static void ScheduleLivenessCheck(string exe, string args, string cwd, int window, int pid)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    int exitCode = 0;
                    bool died = false;

                    try
                    {
                        using (Process p = Process.GetProcessById(pid))
                            died = p.WaitForExit(1200);
                        if (died)
                        {
                            try
                            {
                                using (Process p2 = Process.GetProcessById(pid)) { }
                            }
                            catch { }
                        }
                    }
                    catch (ArgumentException) { died = true; }   // 已经不存在了

                    if (!died) return;

                    // 等一小会儿再取退出码（进程刚退时句柄可能还在收尾）
                    if (exitCode == 0) exitCode = TryGetExitCode(pid);

                    if (exitCode == 0)
                    {
                        // 退出码 0 有两种可能：
                        //   a) 存根/启动器把自己转交给了真正的主程序（正常，比如 Win11 的 notepad.exe）；
                        //   b) 被单实例机制吞掉，窗口根本没出现。
                        // 判断办法：看看这个程序现在到底有没有在运行。
                        //   在运行 → 是 (a)，别动，否则会开出第二个窗口；
                        //   没运行 → 是 (b)，补一次 ShellExecute（等价于双击）再试。
                        if (IsRunning(exe))
                        {
                            Logging.Write(LogLevel.INFO,
                                "  （该进程已退出，退出码 0：主程序已在运行，属正常）");
                            return;
                        }

                        Logging.Write(LogLevel.WARN,
                            "  ⚠ 进程启动后以退出码 0 结束，但检测不到该程序在运行（窗口可能没出来）");
                        if (!AutoRetryIfExited) return;

                        Logging.Write(LogLevel.INFO, "  改用 ShellExecute 重试（和资源管理器里双击等价）…");
                        LaunchResult r0 = ShellExecute("open", exe, args, cwd, window);
                        if (r0.Ok) Logging.Write(LogLevel.OK, "  ✔ 已用 ShellExecute 重新启动，PID " + r0.ProcessId);
                        else Logging.Write(LogLevel.ERR, "  ✖ ShellExecute 也没成功：" + r0.Message);
                        return;
                    }

                    Logging.Write(LogLevel.WARN, string.Format(
                        "  ⚠ 进程 {0} 启动后立刻退出，退出码 0x{1:X8}（{2}）",
                        pid, exitCode, DescribeExitCode(exitCode)));

                    if (!AutoRetryIfExited) return;

                    Logging.Write(LogLevel.INFO, "  改用 ShellExecute 重试（和资源管理器里双击等价）…");
                    LaunchResult r2 = ShellExecute("open", exe, args, cwd, window);
                    if (r2.Ok) Logging.Write(LogLevel.OK, "  ✔ 已用 ShellExecute 重新启动，PID " + r2.ProcessId);
                    else Logging.Write(LogLevel.ERR, "  ✖ ShellExecute 也没成功：" + r2.Message);
                }
                catch { }
            });
        }

        private static int TryGetExitCode(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return p.ExitCode;
            }
            catch { return 0; }
        }

        private static string DescribeExitCode(int code)
        {
            switch (code)
            {
                case unchecked((int)0xC0000142): return "DLL 初始化失败，常见原因：缺少 VC++ 运行库、程序位数不匹配、或以错误身份运行";
                case unchecked((int)0xC0000135): return "缺少 .NET/依赖库";
                case unchecked((int)0xC0000005): return "访问冲突";
                case unchecked((int)0x80070005): return "拒绝访问（可能需要在管理员下运行）";
                default: return "非 0 退出码";
            }
        }

        /// <summary>ShellExecuteEx 启动，用于 shell: 目标、URL、文件夹、需要提权的程序。</summary>
        private static LaunchResult ShellExecute(string verb, string file, string args, string cwd, int window)
        {
            var info = new SHELLEXECUTEINFO();
            info.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
            info.fMask = SEE_MASK_NOCLOSEPROCESS;
            info.lpVerb = verb;
            info.lpFile = file;
            info.lpParameters = Helper.HasText(args) ? args : null;
            info.lpDirectory = (Helper.HasText(cwd) && SafeDirExists(cwd)) ? cwd : null;
            info.nShow = ShowFlag(window);

            bool ok = ShellExecuteEx(info);
            int pid = 0;
            if (ok && info.hProcess != IntPtr.Zero)
            {
                try { pid = GetProcessId(info.hProcess); } catch { }
                try { CloseHandle(info.hProcess); } catch { }
            }

            if (ok) return LaunchResult.Good("已启动（ShellExecute）", pid, 0);

            // ------------------------------------------------------------
            // 失败时的错误码要从 SHELLEXECUTEINFO.hInstApp 取，不是 GetLastWin32Error。
            // hInstApp 在失败时是 ShellExecute 时代的 SE_ERR_* 码（2=找不到文件、5=拒绝访问…）；
            // GetLastWin32Error 经常是上一次调用留下的垃圾值（比如莫名其妙报"1223 UAC 取消"）。
            // ------------------------------------------------------------
            int se = 0;
            try { se = (int)info.hInstApp.ToInt64(); } catch { }
            int err = Marshal.GetLastWin32Error();

            if (verb == "runas" && (se == 5 || err == ERROR_CANCELLED))
            {
                var c = LaunchResult.Fail("UAC 授权被取消，该程序未启动。");
                c.UserCancelled = true;
                return c;
            }

            if (se > 0 && se <= 32)
                return LaunchResult.Fail("ShellExecute 失败（SE_ERR " + se + "：" + DescribeSeErr(se) + "）");

            if (err == ERROR_ELEVATION_REQUIRED) return LaunchResult.Fail("该程序需要管理员权限，但提权被系统拒绝。");

            return LaunchResult.Fail("ShellExecute 失败（错误码 " + err + "：" +
                new System.ComponentModel.Win32Exception(err).Message + "）");
        }

        /// <summary>ShellExecute 系列的老式错误码。</summary>
        private static string DescribeSeErr(int se)
        {
            switch (se)
            {
                case 0: return "系统资源不足";
                case 2: return "找不到文件（SE_ERR_FNF）";
                case 3: return "找不到路径（SE_ERR_PNF）";
                case 5: return "拒绝访问（SE_ERR_ACCESSDENIED）";
                case 8: return "内存不足（SE_ERR_OOM）";
                case 26: return "共享冲突（SE_ERR_SHARE）";
                case 27: return "文档关联不完整（SE_ERR_ASSOCINCOMPLETE）";
                case 30: return "DDE 忙（SE_ERR_DDEBUSY）";
                case 31: return "没有关联的程序（SE_ERR_NOASSOC）";
                case 32: return "找不到 DLL（SE_ERR_DLLNOTFOUND）";
                default: return "ShellExecute 错误";
            }
        }

        private static LaunchResult ShellOnly(string target, string args, string cwd, int window)
        {
            return ShellExecute("open", target, args, cwd, window);
        }

        /// <summary>
        /// 用资源管理器打开一个文件夹。
        ///
        /// 注意：**不要**去 CreateProcess/Start 一个 explorer.exe。
        /// 那样等于手动再拉一个资源管理器进程，在某些系统上会报
        /// "explorer.exe - 应用程序无法正常启动 (0xc0000142)"（DLL 初始化失败，用户真实遇到过）。
        /// 正确做法是把文件夹路径交给 shell，由它转交已经运行的那个 Explorer 实例
        /// —— 和你在资源管理器地址栏里输入路径完全等价。
        /// </summary>
        public static LaunchResult OpenInExplorer(string folder)
        {
            if (!Helper.HasText(folder)) return LaunchResult.Fail("路径为空");
            if (!SafeDirExists(folder)) return LaunchResult.Fail("文件夹不存在：" + folder);
            return ShellExecute("open", folder, "", folder, 0);
        }

        [DllImport("kernel32.dll")]
        private static extern int GetProcessId(IntPtr handle);

        private static bool SafeDirExists(string d)
        {
            try { return Helper.HasText(d) && Directory.Exists(d); }
            catch { return false; }
        }

        private static int ShowFlag(int windowState)
        {
            switch (windowState)
            {
                case 1: return 3;   // SW_SHOWMAXIMIZED
                case 2: return 7;   // SW_SHOWMINNOACTIVE（不像 SW_SHOWMINIMIZED 那样抢焦点）
                default: return 1;  // SW_SHOWNORMAL
            }
        }

        // ----------------------------------------------------- 串行启动调度

        /// <summary>
        /// 串行启动一批程序：一个接一个，每个之间等待它自己的延迟秒数。
        /// wait 回调用于界面把「已启动 / 正在等待」反映出来，返回 false 表示用户要停。
        /// </summary>
        public static void RunSequence(List<AppEntry> items, AppConfig cfg,
            Func<bool> shouldStop, Action<AppEntry, int, int> onProgress,
            Action<AppEntry, LaunchResult> onResult, Action<int, int, string> onWait)
        {
            int total = items.Count;
            for (int i = 0; i < total; i++)
            {
                if (shouldStop()) return;
                AppEntry e = items[i];

                if (onProgress != null) onProgress(e, i + 1, total);

                LaunchResult r;
                try
                {
                    r = Launch(e, cfg.FoldersInExplorer);
                }
                catch (Exception ex)
                {
                    Logging.Exception("启动「" + e.Name + "」", ex);
                    r = LaunchResult.Fail("启动时发生异常：" + ex.Message);
                }

                if (onResult != null) onResult(e, r);

                if (i >= total - 1) continue;

                int wait = e.DelaySeconds;
                if (wait <= 0) continue;

                if (onWait != null) onWait(i + 2, total, e.Name);

                // 可中断的等待：每 100ms 检查一次停止标志
                int elapsed = 0;
                while (elapsed < wait * 1000)
                {
                    if (shouldStop()) return;
                    int slice = Math.Min(100, wait * 1000 - elapsed);
                    Thread.Sleep(slice);
                    elapsed += slice;
                }
            }
        }
    }
}
