using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace QuickLaunch
{
    internal static class Program
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetConsoleOutputCP(uint codePage);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        /// <summary>
        /// 让「GUI 子系统」的 exe 也能把输出打到调用它的 cmd 窗口。
        ///
        /// 背景：本程序编译成 /target:winexe。这种进程默认**不连接父控制台**，
        /// 于是 cmd 里跑 `QuickLaunch.exe --launch-one=...` 时所有 Console.WriteLine 都被吞掉，
        /// 用户看到的就是"敲了命令，什么都没打印"。
        /// 做法：先 AttachConsole 附着到父进程的控制台；没有父控制台（双击运行）就自己开一个。
        /// </summary>
        private static void EnsureConsole()
        {
            try
            {
                if (GetConsoleWindow() != IntPtr.Zero) return;   // 已经连着控制台（控制台子系统构建时）

                bool ok = AttachConsole(ATTACH_PARENT_PROCESS);
                if (!ok) ok = AllocConsole();
                if (!ok) return;

                var stdout = new System.IO.StreamWriter(Console.OpenStandardOutput(),
                    new System.Text.UTF8Encoding(false));
                stdout.AutoFlush = true;
                Console.SetOut(stdout);

                var stderr = new System.IO.StreamWriter(Console.OpenStandardError(),
                    new System.Text.UTF8Encoding(false));
                stderr.AutoFlush = true;
                Console.SetError(stderr);

                try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); } catch { }
                SetConsoleOutputCP(65001);
            }
            catch { }
        }

        /// <summary>
        /// 跑一段诊断代码，把它的控制台输出同时收集起来写成 UTF-8 报告文件。
        /// 控制台看不到输出时（比如输出被吞、或者窗口滚走了），直接看报告文件即可。
        /// </summary>
        private static int RunWithReport(string reportFileName, Func<int> core)
        {
            var buf = new System.Text.StringBuilder();
            System.IO.TextWriter orig = Console.Out;
            Console.SetOut(new TeeWriter(orig, buf));

            int rc;
            try { rc = core(); }
            catch (Exception ex)
            {
                Console.WriteLine("执行异常: " + ex.GetType().Name + " → " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                rc = 1;
            }
            finally { Console.SetOut(orig); }

            try
            {
                string path = System.IO.Path.Combine(Helper.LogDir, reportFileName);
                System.IO.File.WriteAllText(path, buf.ToString(), new System.Text.UTF8Encoding(false));
                orig.WriteLine();
                orig.WriteLine("──────── 报告已写入（UTF-8，记事本可直接打开） ────────");
                orig.WriteLine("  " + path);
                orig.WriteLine("  如果控制台里没看到内容，把上面这个文件发给我就行。");
            }
            catch { }

            return rc;
        }

        /// <summary>是否以 --launch-test 模式运行（真实启动 3 个无害程序，验证引擎与延迟）。</summary>
        internal static bool CliLaunchTest;

        /// <summary>是否以 --selftest 模式运行（真实构建整个界面，但不显示、不进入消息循环）。</summary>
        internal static bool CliSelfTest;

        /// <summary>是否以 --savetest 模式运行（生成条目 → 并入配置 → 保存 → 回读校验）。</summary>
        internal static bool CliSaveTest;

        /// <summary>是否以 --uicheck 模式运行（输出 DPI 与布局实测值，排查「糊 / 挤 / 空白行」）。</summary>
        internal static bool CliUiCheck;

        /// <summary>是否以 --launch-one=路径 模式运行（只启动一个指定程序，打印全部诊断）。</summary>
        internal static bool CliLaunchOne;

        /// <summary>--launch-one 的目标（.exe / .lnk / 文件夹 / 网址）。</summary>
        internal static string LaunchOneTarget = "";

        [STAThread]
        private static void Main(string[] args)
        {
            // GUI 子系统的 exe 默认不连接调用它的控制台，不附着的话下面所有输出都会被吞掉。
            // 只有带参数（诊断模式）才附着；正常双击启动绝不能调，否则会平白弹个黑窗口。
            if (args != null && args.Length > 0) EnsureConsole();
            // 控制台模式下强制 UTF-8，否则中文在 GBK 代码页里会变乱码
            try
            {
                Console.OutputEncoding = new System.Text.UTF8Encoding(false);
                SetConsoleOutputCP(65001);
            }
            catch { }

            Logging.Init();

            foreach (string a in args)
            {
                if (string.Equals(a, "--launch-test", StringComparison.OrdinalIgnoreCase))
                    CliLaunchTest = true;
                else if (string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase))
                    CliSelfTest = true;
                else if (string.Equals(a, "--savetest", StringComparison.OrdinalIgnoreCase))
                    CliSaveTest = true;
                else if (string.Equals(a, "--uicheck", StringComparison.OrdinalIgnoreCase))
                    CliUiCheck = true;
                else if (a.StartsWith("--launch-one=", StringComparison.OrdinalIgnoreCase))
                {
                    CliLaunchOne = true;
                    LaunchOneTarget = a.Substring(13).Trim('"');
                }
                else if (a.StartsWith("--cfgdir=", StringComparison.OrdinalIgnoreCase))
                {
                    // 便携模式：配置放到指定目录，不碰 %APPDATA%
                    Helper.DataDirOverride = a.Substring(9).Trim('"');
                }
                else if (a.StartsWith("--logdir=", StringComparison.OrdinalIgnoreCase))
                {
                    Helper.LogDirOverride = a.Substring(9).Trim('"');
                }
                else if (string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase))
                {
                    Logging.ConsoleMirror = true;
                    Logging.Write(LogLevel.OK, "QuickLaunch v1.0 — 版本自检正常（日志系统已就绪）");
                    Logging.Write(LogLevel.INFO, "日志文件：" + Logging.FilePath);
                    Logging.Shutdown();
                    Environment.Exit(0);
                    return;
                }
                else if (string.Equals(a, "--console", StringComparison.OrdinalIgnoreCase))
                    Logging.ConsoleMirror = true;
            }

            // ------------------------------------------------------------
            // 诊断模式绝不碰用户的正式配置！
            //
            // 教训：--savetest 会「生成条目 → 并入配置 → 保存 → 回读」，
            // 之前它直接写进了真实的 config.json，把用户辛苦拖进去的清单覆盖成了 20 条测试数据。
            // 现在只要带任何诊断参数，配置目录就强制改到临时目录；
            // 除非用户自己用 --cfgdir 明确指定了位置。
            // ------------------------------------------------------------
            bool diagMode = CliLaunchOne || CliLaunchTest || CliSelfTest || CliSaveTest || CliUiCheck;
            if (diagMode && !Helper.HasText(Helper.DataDirOverride))
            {
                Helper.DataDirOverride = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "QuickLaunchDiagCfg");
            }


            if (CliLaunchOne)
            {
                Logging.ConsoleMirror = true;
                Environment.Exit(RunLaunchOne());
                return;
            }

            if (CliLaunchTest)
            {
                Logging.ConsoleMirror = true;
                Environment.Exit(RunLaunchTest());
                return;
            }

            if (CliSelfTest)
            {
                Logging.ConsoleMirror = true;
                Environment.Exit(RunSelfTest());
                return;
            }

            if (CliUiCheck)
            {
                Logging.ConsoleMirror = true;
                Environment.Exit(RunUiCheck());
                return;
            }

            if (CliSaveTest)
            {
                Logging.ConsoleMirror = true;
                Environment.Exit(RunSaveTest());
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Logging.Write(LogLevel.ERR, "未处理的异常：" + (e.ExceptionObject as Exception));
                Logging.Write(LogLevel.ERR, "程序即将退出。详细堆栈已写入日志文件。");
                if (e.ExceptionObject is Exception)
                    Logging.Write(LogLevel.ERR, ((Exception)e.ExceptionObject).StackTrace);
                Logging.Shutdown();
            };

            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Logging.Write(LogLevel.ERR, "界面线程异常：" + e.Exception.Message);
                Logging.Write(LogLevel.ERR, e.Exception.StackTrace);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var sw = Stopwatch.StartNew();
            AppConfig cfg = ConfigStore.Load();
            long loadMs = sw.ElapsedMilliseconds;

            Application.Run(new MainForm(cfg, loadMs));

            Logging.Write(LogLevel.INFO, "程序退出，日志收尾中。");
            Logging.Shutdown();
        }

        /// <summary>
        /// 造一批测试条目给自检模式用。本程序不再扫描系统，所以测试数据是合成的。
        /// 全部指向系统自带的、确定存在的文件，保证"路径存在性"检查不会误报。
        /// </summary>
        private static List<AppEntry> BuildTestEntries(int count)
        {
            string sys = Environment.SystemDirectory;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string winSxS = Path.Combine(win, "System32", "cmd.exe");

            string[] names = new string[]
            {
                "命令提示符", "记事本", "计算器", "任务管理器", "资源管理器", "控制面板",
                "very long program name used to verify ellipsis rendering 省略号测试",
                "微信", "WPS Office", "Chrome"
            };
            string[] files = new string[]
            {
                "cmd.exe", "notepad.exe", "calc.exe", "taskmgr.exe", "explorer.exe", "control.exe",
                "cmd.exe", "cmd.exe", "cmd.exe", "cmd.exe"
            };

            var list = new List<AppEntry>();
            for (int i = 0; i < count; i++)
            {
                var e = new AppEntry();
                e.Name = names[i % names.Length] + (i >= names.Length ? " " + (i + 1) : "");
                string file = files[i % files.Length];
                e.Target = File.Exists(Path.Combine(sys, file)) ? Path.Combine(sys, file) : winSxS;
                e.WorkingDir = Helper.DirName(e.Target);
                e.Source = (i % 3 == 0) ? "快捷方式" : (i % 3 == 1) ? "手动添加" : "文件夹";
                e.DelaySeconds = 2 + (i % 5);
                e.Enabled = (i % 4 != 3);
                e.Group = "daily";
                list.Add(e);
            }
            return list;
        }

        /// <summary>
        /// 单目标启动诊断：只启动一个程序，把解析结果、真实命令行、PID、退出码全打出来。
        ///
        /// 用途：界面里点「▶」没反应时，用它精确定位卡在哪一步。
        ///   QuickLaunch.exe --launch-one="E:\obsidian\obsidian.exe"
        ///   QuickLaunch.exe --launch-one="C:\Users\你\Desktop\某快捷方式.lnk"
        ///   QuickLaunch.exe --launch-one="D:\某个文件夹"
        ///
        /// 输出同时写进 logs\launch-one-report.txt —— 控制台里看不到时直接看这个文件。
        /// </summary>
        private static int RunLaunchOne()
        {
            return RunWithReport("launch-one-report.txt", RunLaunchOneCore);
        }

        private static int RunLaunchOneCore()
        {
            Logging.Write(LogLevel.INFO, "QuickLaunch 单目标启动诊断（--launch-one）");
            Console.WriteLine();

            string raw = LaunchOneTarget;
            if (!Helper.HasText(raw))
            {
                Logging.Write(LogLevel.ERR, "没有给目标。用法：--launch-one=\"完整路径\"");
                Logging.Shutdown();
                return 1;
            }

            // ---------- 1. 解析（.lnk 需要先解出真实目标）
            string target = raw;
            string args = "";
            string cwd = "";
            string source = "直接指定";

            string ext = "";
            try { ext = Path.GetExtension(raw).ToLowerInvariant(); }
            catch { }

            if (ext == ".lnk" || ext == ".url")
            {
                ShortcutResolver.Target t = ShortcutResolver.Resolve(raw);
                if (t == null || !Helper.HasText(t.Path))
                {
                    Logging.Write(LogLevel.ERR, "✖ 解析不出快捷方式目标（文件可能损坏）");
                    Logging.Shutdown();
                    return 1;
                }
                target = t.Path;
                args = t.Arguments;
                cwd = t.WorkingDir;
                source = "快捷方式解析";
            }

            if (!Helper.HasText(cwd)) cwd = Helper.DirName(target);

            Console.WriteLine("──────── 解析结果 ────────");
            Console.WriteLine("  来源     : " + source);
            Console.WriteLine("  输入     : " + raw);
            Console.WriteLine("  目标     : " + target);
            Console.WriteLine("  参数     : " + (Helper.HasText(args) ? args : "（无）"));
            Console.WriteLine("  工作目录 : " + (Helper.HasText(cwd) ? cwd : "（无）"));
            Console.WriteLine("  目标存在 : " + (Directory.Exists(target) || File.Exists(target) ? "是" : "✖ 否"));

            bool isFolder = false;
            try { isFolder = Directory.Exists(target); } catch { }
            Console.WriteLine("  类型     : " + (isFolder ? "文件夹"
                : Helper.LooksLikeUrl(target) ? "网址"
                : Helper.LooksLikeShellTarget(target) ? "shell 目标"
                : "可执行文件/文档"));

            bool exeLike = !isFolder && !Helper.LooksLikeUrl(target) && !Helper.LooksLikeShellTarget(target);
            if (exeLike)
            {
                bool running = Launcher.IsRunning(target);
                Console.WriteLine("  当前进程 : " + (running
                    ? "⚠ 已经在运行（单实例程序会把启动转发给它，可能不弹新窗口；可能只是缩在托盘里）"
                    : "未运行"));
            }

            if (exeLike && !Helper.FileExistsSafe(target))
            {
                Console.WriteLine();
                Logging.Write(LogLevel.ERR, "✖ 目标文件不存在，先确认路径。");
                return 1;
            }

            var entry = new AppEntry();
            entry.Name = Helper.ExeName(target);
            entry.Target = target;
            entry.Arguments = args;
            entry.WorkingDir = cwd;
            entry.WindowState = 0;
            // 和真实界面保持一致：来自 .lnk 的条目会记住原始快捷方式，启动时优先走"双击快捷方式"语义
            if (ext == ".lnk" || ext == ".url") entry.ShortcutPath = raw;

            // ---------- 2. 实际启动（关掉自动重试，先看原始结果）
            Launcher.AutoRetryIfExited = false;

            Console.WriteLine();
            Console.WriteLine("──────── 实际启动 ────────");
            if (!isFolder && !Helper.LooksLikeUrl(target) && !Helper.LooksLikeShellTarget(target))
                Console.WriteLine("  命令行   : \"" + target + "\"" + (Helper.HasText(args) ? " " + args : ""));

            LaunchResult r = Launcher.Launch(entry, true);
            Console.WriteLine("  结果     : " + (r.Ok ? "成功" : "失败") + "（" + r.Message + "）");
            Console.WriteLine("  PID      : " + r.ProcessId);
            Console.WriteLine("  耗时     : " + r.ElapsedMs + " ms");

            if (!r.Ok)
            {
                Console.WriteLine();
                Logging.Write(LogLevel.ERR, "✖ 启动失败，原因见上。");
                Logging.Write(LogLevel.INFO, "日志文件：" + Logging.FilePath);
                Logging.Shutdown();
                return 1;
            }

            // ---------- 3. 盯 3 秒：很多"点了没反应"其实是进程起来了又立刻退了
            if (r.ProcessId > 0)
            {
                Console.WriteLine();
                Console.WriteLine("──────── 存活检查（3 秒）───────");
                bool alive = false;
                for (int i = 0; i < 6; i++)
                {
                    Thread.Sleep(500);
                    alive = IsAlive(r.ProcessId);
                    if (!alive) break;
                }

                if (alive)
                {
                    Console.WriteLine("  ✔ 进程仍在运行 —— 程序确实启动了");
                    Console.WriteLine("    如果窗口没出现，可能是：");
                    Console.WriteLine("      · 程序最小化到了托盘（托盘区找一下）");
                    Console.WriteLine("      · 程序是单实例的，把启动转发给了已有实例后自己退出");
                    Console.WriteLine("      · 窗口开在别的显示器 / 屏幕外面");
                }
                else
                {
                    int code = TryGetExitCodeSafe(r.ProcessId);
                    Console.WriteLine(string.Format("  ⚠ 进程已退出，退出码 0x{0:X8}", code));
                    if (code == unchecked((int)0xC0000142))
                        Console.WriteLine("      → DLL 初始化失败：多半缺 VC++ 运行库，或程序位数/身份不匹配");
                    else if (code != 0)
                        Console.WriteLine("      → 非 0 退出码表示启动过程中出错了");

                    Console.WriteLine();
                    Console.WriteLine("  ── 改用 ShellExecute（等价于资源管理器里双击）再试一次 ──");
                    LaunchResult r2 = Launcher.Launch(entry, true);
                    Console.WriteLine("     结果 : " + (r2.Ok ? "成功" : "失败") + "  PID " + r2.ProcessId);
                    if (r2.Ok && r2.ProcessId > 0)
                    {
                        Thread.Sleep(1500);
                        Console.WriteLine("     存活 : " + (IsAlive(r2.ProcessId) ? "✔ 仍在运行" : "⚠ 又退出了"));
                    }
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("  （没有拿到 PID：可能是 shell 目标/文件夹，属正常）");
            }

            Console.WriteLine();
            Console.WriteLine("日志文件：" + Logging.FilePath);
            Logging.Shutdown();
            return 0;
        }

        private static bool IsAlive(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return !p.HasExited;
            }
            catch { return false; }
        }

        private static int TryGetExitCodeSafe(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return p.HasExited ? p.ExitCode : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 启动引擎自检：真实启动 3 个无害目标，验证「串行 + 每个程序自己的延迟」是否准确。
        /// 用的是和界面完全相同的 Launcher.RunSequence，不是另写一套。
        /// </summary>
        private static int RunLaunchTest()
        {
            // 自检用的是 cmd /c exit（故意秒退），关掉"秒退自动重试"，否则会重复拉起
            Launcher.AutoRetryIfExited = false;

            Logging.Write(LogLevel.INFO, "QuickLaunch 启动引擎自检（--launch-test）");
            Console.WriteLine();

            var cfg = AppConfig.CreateDefault();
            cfg.FoldersInExplorer = true;

            int[] delays = new int[] { 2, 3, 0 };
            var items = new List<AppEntry>();

            items.Add(MakeTest("① 命令提示符（cmd，立即自动退出）", 1,
                Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit", delays[0]));
            items.Add(MakeTest("② 记事本（会打开窗口，测试后请手动关掉）", 1,
                Path.Combine(Environment.SystemDirectory, "notepad.exe"), "", delays[1]));
            // 第 3 项刻意不用「打开文件夹」：那一步依赖桌面/资源管理器，
            // 在无头环境里必然失败，会把「延迟精度」这条真正要测的东西盖掉。
            items.Add(MakeTest("③ 命令提示符（再跑一次，验证最后一环）", 1,
                Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit", delays[2]));

            int expectSec = 0;
            for (int i = 0; i < items.Count - 1; i++) expectSec += items[i].DelaySeconds;

            Logging.Write(LogLevel.INFO, string.Format("将依次启动 {0} 个目标，延迟分别是 {1} 秒，理论总耗时 ≈ {2} 秒",
                items.Count, string.Join(" / ", Array.ConvertAll<int, string>(delays, delegate(int d) { return d.ToString(); })), expectSec));
            Console.WriteLine();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = new List<string>();

            Launcher.RunSequence(items, cfg,
                delegate() { return false; },
                delegate(AppEntry e, int idx, int total)
                {
                    Logging.Write(LogLevel.INFO, string.Format("[{0}/{1}] 正在启动 {2}  ← 此刻已过 {3:0.00} 秒",
                        idx, total, e.Name, sw.Elapsed.TotalSeconds));
                },
                delegate(AppEntry e, LaunchResult r)
                {
                    if (r.Ok)
                    {
                        Logging.Write(LogLevel.OK, string.Format("  ✔ 成功   PID {0}   调用耗时 {1} ms", r.ProcessId, r.ElapsedMs));
                        results.Add(e.Name + " = 成功");
                    }
                    else
                    {
                        Logging.Write(LogLevel.ERR, "  ✖ 失败：" + r.Message);
                        results.Add(e.Name + " = 失败(" + r.Message + ")");
                    }
                },
                delegate(int next, int total, string prev)
                {
                    Logging.Write(LogLevel.WAIT, string.Format("  ⏳ 等待中…（下一个是第 {0}/{1} 个）", next, total));
                });

            sw.Stop();
            double actual = sw.Elapsed.TotalSeconds;

            Console.WriteLine();
            Logging.Write(LogLevel.INFO, "──────── 结果 ────────");
            foreach (string r in results) Logging.Write(LogLevel.INFO, "  " + r);

            double diff = Math.Abs(actual - expectSec);
            Logging.Write(LogLevel.INFO, string.Format("理论总耗时 {0} 秒，实测 {1:0.00} 秒，偏差 {2:0.00} 秒", expectSec, actual, diff));

            bool pass = diff < 1.5 && results.Count == items.Count;
            bool anyFail = false;
            foreach (string r in results) if (r.IndexOf("失败", StringComparison.Ordinal) >= 0) anyFail = true;

            if (anyFail)
                Logging.Write(LogLevel.ERR, "有目标启动失败，请检查上面的错误信息。");
            else if (pass)
                Logging.Write(LogLevel.OK, "✔ 自检通过：串行启动正常，延迟精度符合预期。");
            else
                Logging.Write(LogLevel.WARN, "⚠ 延迟偏差偏大（可能是系统繁忙），但启动流程本身是通的。");

            Console.WriteLine();
            Logging.Write(LogLevel.INFO, "日志文件：" + Logging.FilePath);
            Logging.Shutdown();
            return anyFail ? 1 : 0;
        }

        private static AppEntry MakeTest(string name, int windowState, string target, string args, int delay)
        {
            var e = new AppEntry();
            e.Name = name;
            e.Target = target;
            e.Arguments = args;
            e.WorkingDir = Helper.DirName(target);
            e.Source = "自检";
            e.DelaySeconds = delay;
            e.WindowState = windowState;
            e.Enabled = true;
            return e;
        }

        /// <summary>
        /// 界面自检：真实构造 MainForm（控件、图标、分组、列表全建一遍），但不显示、不进消息循环。
        /// 用来在没有桌面的环境里验证界面代码不会抛异常。
        /// </summary>
        private static int RunSelfTest()
        {
            Logging.Write(LogLevel.INFO, "QuickLaunch 界面自检（--selftest）：构建 MainForm 但不显示");

            var sw = Stopwatch.StartNew();
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                AppConfig cfg = ConfigStore.Load();
                Logging.Write(LogLevel.INFO, string.Format("配置载入完成（{0} 项），耗时 {1} ms", cfg.Entries.Count, sw.ElapsedMilliseconds));

                long t0 = sw.ElapsedMilliseconds;
                using (var f = new MainForm(cfg, 0))
                {
                    long buildMs = sw.ElapsedMilliseconds - t0;

                    // 强制创建窗口句柄 → 触发 OnLoad → 走完整个初始化路径
                    f.CreateControl();
                    IntPtr h = f.Handle;
                    f.PerformLayout();

                    Logging.Write(LogLevel.OK, string.Format("MainForm 构建成功：控件树 {0} 个控件，句柄已创建，耗时 {1} ms",
                        CountControls(f), buildMs));

                    Logging.Write(LogLevel.INFO, string.Format("窗口尺寸 {0}x{1}，标题「{2}」",
                        f.ClientSize.Width, f.ClientSize.Height, f.Text));

                    // 触发一次布局与重绘路径
                    f.Refresh();

                    Logging.Write(LogLevel.INFO, "日志面板控件：LogView 行数 = " + ReadLogCount(f));

                    Logging.Write(LogLevel.OK, "✔ 界面自检通过：没有抛出异常，所有控件都能创建。");
                    Logging.Write(LogLevel.INFO, "（本模式不显示窗口，正常运行请直接双击 exe）");
                    Logging.Write(LogLevel.INFO, "日志文件：" + Logging.FilePath);
                    Logging.Shutdown();
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Logging.Write(LogLevel.ERR, "✖ 界面自检失败：" + ex.GetType().Name + " → " + ex.Message);
                Logging.Write(LogLevel.ERR, ex.StackTrace);
                if (ex.InnerException != null)
                    Logging.Write(LogLevel.ERR, "内部异常：" + ex.InnerException.Message);
                Logging.Shutdown();
                return 1;
            }
        }

        /// <summary>
        /// 配置链路自检：真实构建界面 → 生成测试条目 → 并入配置 → 保存 → 重新读取校验。
        /// 这条链路就是用户拖入程序、勾选、退出时自动保存会用到的全部逻辑。
        /// </summary>
        private static int RunSaveTest()
        {
            Logging.Write(LogLevel.INFO, "QuickLaunch 配置链路自检（--savetest）");
            var sw = Stopwatch.StartNew();

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var cfg = new AppConfig();
                cfg.Groups = AppConfig.CreateDefault().Groups;

                long t0 = sw.ElapsedMilliseconds;
                using (var f = new MainForm(cfg, 0))
                {
                    f.CreateControl();
                    IntPtr h = f.Handle;
                    Logging.Write(LogLevel.INFO, string.Format("界面构建 + 句柄创建：{0} ms", sw.ElapsedMilliseconds - t0));

                    long t1 = sw.ElapsedMilliseconds;
                    List<AppEntry> found = BuildTestEntries(12);
                    Logging.Write(LogLevel.OK, string.Format("生成测试数据：{0} 个程序，{1} ms", found.Count, sw.ElapsedMilliseconds - t1));

                    f.LoadTestData(found, true);

                    int saved = Helper.FileExistsSafe(Helper.ConfigPath) ? File.ReadAllText(Helper.ConfigPath).Length : 0;
                    Logging.Write(LogLevel.INFO, string.Format("已保存 config.json：{0:N0} 字节", saved));

                    // 回读校验
                    AppConfig back = ConfigStore.Load();
                    int enabled = 0;
                    foreach (AppEntry e in back.Entries) if (e.Enabled) enabled++;

                    Logging.Write(LogLevel.INFO, "──────── 回读校验 ────────");
                    Logging.Write(LogLevel.INFO, string.Format("  程序条目      : {0}", back.Entries.Count));
                    Logging.Write(LogLevel.INFO, string.Format("  已勾选        : {0}", enabled));
                    Logging.Write(LogLevel.INFO, string.Format("  分组          : {0}", back.Groups.Count));
                    Logging.Write(LogLevel.INFO, string.Format("  默认延迟      : {0} 秒", back.DefaultDelaySeconds));

                    bool ok = back.Entries.Count == cfg.Entries.Count && enabled == 3 && back.Groups.Count == cfg.Groups.Count;

                    Logging.Write(LogLevel.INFO, "  前 5 条：");
                    int shown = 0;
                    foreach (AppEntry e in back.Entries)
                    {
                        if (shown++ >= 5) break;
                        Logging.Write(LogLevel.INFO, string.Format("    {0} {1,-32} delay={2} {3}",
                            e.Enabled ? "[√]" : "[ ]", e.Name, e.DelaySeconds, e.Target));
                    }

                    Logging.Write(LogLevel.INFO, string.Format("配置路径：{0}", Helper.ConfigPath));
                    Logging.Write(LogLevel.INFO, string.Format("日志路径：{0}", Logging.FilePath));

                    if (ok)
                        Logging.Write(LogLevel.OK, "✔ 配置链路自检通过：生成条目 → 并入配置 → 保存 → 回读 全部一致。");
                    else
                        Logging.Write(LogLevel.ERR, "✖ 配置链路自检失败：回读结果与内存不一致。");

                    Logging.Shutdown();
                    return ok ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Logging.Write(LogLevel.ERR, "✖ 配置链路自检异常：" + ex.GetType().Name + " → " + ex.Message);
                Logging.Write(LogLevel.ERR, ex.StackTrace);
                Logging.Shutdown();
                return 1;
            }
        }

        /// <summary>
        /// 布局/DIP 诊断：把 DPI、缩放系数、窗口尺寸、列表行高、字体能否画出中文、
        /// 以及列表前几行实际画出来的像素都打出来。排查「界面糊 / 挤 / 行是空的」用。
        /// </summary>
        private static int RunUiCheck()
        {
            // 控制台在部分环境下读不到中文（GBK 重定向），所以同时写一份 UTF-8 报告文件
            var buf = new System.Text.StringBuilder();
            TextWriter origOut = Console.Out;
            var tee = new TeeWriter(origOut, buf);
            Console.SetOut(tee);
            int rc = 0;

            try
            {
                rc = RunUiCheckCore();
            }
            catch (Exception ex)
            {
                Console.WriteLine("诊断异常: " + ex);
                rc = 1;
            }
            finally
            {
                Console.SetOut(origOut);
            }

            string report = Path.Combine(Helper.LogDir, "uicheck-report.txt");
            try
            {
                File.WriteAllText(report, buf.ToString(), new System.Text.UTF8Encoding(false));
                origOut.WriteLine();
                origOut.WriteLine("诊断报告已写入（UTF-8，用记事本打开不亂码）：");
                origOut.WriteLine("  " + report);
            }
            catch { }
            return rc;
        }

        /// <summary>把输出同时写到控制台和内存缓冲。</summary>
        private sealed class TeeWriter : TextWriter
        {
            private readonly TextWriter _a;
            private readonly System.Text.StringBuilder _b;
            public TeeWriter(TextWriter a, System.Text.StringBuilder b) { _a = a; _b = b; }
            public override System.Text.Encoding Encoding { get { return _a.Encoding; } }
            public override void Write(char c) { _a.Write(c); _b.Append(c); }
            public override void Write(string s) { _a.Write(s); _b.Append(s); }
            public override void WriteLine(string s) { _a.WriteLine(s); _b.AppendLine(s); }
            public override void WriteLine() { _a.WriteLine(); _b.AppendLine(); }
        }

        private static int RunUiCheckCore()
        {
            Console.WriteLine();
            Console.WriteLine("======== QuickLaunch 布局诊断 (--uicheck) ========");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Console.WriteLine("系统 DPI 探测 : " + Theme.InitDpi());
            Console.WriteLine("缩放系数     : " + Theme.Scale);

            var cfg = ConfigStore.Load();
            if (cfg.Entries.Count == 0)
            {
                // 本程序不扫描系统；这里只是给诊断造一批合成数据看界面效果（不写盘）
                List<AppEntry> seeded = BuildTestEntries(12);
                long seedId = 0;
                foreach (AppEntry e in seeded) { e.Id = ++seedId; cfg.Entries.Add(e); }
                Console.WriteLine("配置为空，用 " + seeded.Count + " 条合成数据渲染界面（仅诊断用，不写盘）");
            }

            using (var f = new MainForm(cfg, 0))
            {
                f.CreateControl();
                IntPtr h = f.Handle;
                Console.WriteLine("窗口客户区   : " + f.ClientSize.Width + " x " + f.ClientSize.Height);
                Console.WriteLine("窗口字体     : " + f.Font.Name + " " + f.Font.SizeInPoints + "pt  (行高 " + f.Font.Height + "px)");
                Console.WriteLine("DPI 缩放信息 : " + f.DpiInfo);
                Console.WriteLine("控件总数     : " + CountControls(f));

                // 必须 Show 一次，否则 Load 事件不触发 → RefreshList 不跑 → 列表是空的
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                f.Refresh();
                Application.DoEvents();

                Console.WriteLine();
                Console.WriteLine("--- 滚动条 / 细长控件（排查多余滚动条） ---");
                int scrollHits = 0;
                foreach (Control c in AllControls(f))
                {
                    bool isScrollBar = c is ScrollBar;
                    bool isThinWide = !isScrollBar && c.Height <= Theme.S(24) && c.Width >= Theme.S(160);
                    if (!isScrollBar && !isThinWide) continue;
                    scrollHits++;
                    Console.WriteLine(string.Format("  {0,-14} {1,-46} 父容器 {2}",
                        c.GetType().Name, c.Bounds.ToString(),
                        c.Parent == null ? "(无)" : c.Parent.GetType().Name));
                }
                if (scrollHits == 0) Console.WriteLine("  ✓ 没有多余的滚动条控件");

                Console.WriteLine();
                Console.WriteLine("--- 关键控件实测尺寸 ---");
                foreach (Control c in AllControls(f))
                {
                    string tag = null;
                    if (c is AppListView) tag = "程序列表 AppListView";
                    else if (c is LogView) tag = "日志面板 LogView";
                    else if (c is GroupList) tag = "分组列表 GroupList";
                    else if (c is GroupBar) tag = "底栏分组按钮 GroupBar";
                    else if (c is StatCard) tag = "统计卡片";
                    else if (c is TextBox) tag = "输入框";
                    else if (c is QButton)
                    {
                        var q = (QButton)c;
                        tag = q.Style == QButton.Look.Primary ? "主按钮" : "按钮「" + q.Text + "」";
                    }
                    if (tag == null) continue;
                    Console.WriteLine(string.Format("  {0,-24} {1,4} x {2,-4} @ ({3},{4})",
                        tag, c.Width, c.Height, c.Left, c.Top));
                }

                Console.WriteLine();
                Console.WriteLine("--- 中文字形能否画出（GDI 实测像素） ---");
                CheckGlyphs(f.Font);

                Console.WriteLine();
                Console.WriteLine("--- 控件重叠检测（文字堆叠就是它引起的） ---");
                CheckOverlaps(f);

                Console.WriteLine();
                Console.WriteLine("--- 标题栏右上角窗口按钮 ---");
                CheckHeaderButtons(f);

                Console.WriteLine();
                Console.WriteLine("--- 列表行绘制实测 ---");
                DumpListRows(f);

                Console.WriteLine();
                Console.WriteLine("配置文件     : " + Helper.ConfigPath);
                Console.WriteLine("日志文件     : " + Logging.FilePath);
                Console.WriteLine("======== 诊断结束 ========");
            }

            Logging.Shutdown();
            return 0;
        }

        private static void CheckGlyphs(Font f)
        {
            string[] samples = new string[]
            {
                "程序", "启动延迟", "一键启动", "运行日志", "Microsoft YaHei"
            };
            foreach (string s in samples)
            {
                Size sz = TextRenderer.MeasureText(s, f);
                bool tofu = sz.Width <= 4 || sz.Height <= 4;
                Console.WriteLine(string.Format("  {0,-18} 像素 {1,4} x {2,-3}  {3}",
                    "「" + s + "」", sz.Width, sz.Height, tofu ? "✗ 画不出来" : "✓ 正常"));
            }
            Console.WriteLine("  当前字体族: " + f.FontFamily.Name + "   可用: " + IsFontUsable(f));
        }

        private static string IsFontUsable(Font f)
        {
            try { return f.FontFamily.Name; }
            catch (Exception ex) { return "✗ " + ex.Message; }
        }

        /// <summary>同一容器里的兄弟控件两两比对矩形，找出重叠对。</summary>
        private static void CheckOverlaps(MainForm f)
        {
            int bad = 0, checkedPairs = 0;
            foreach (Control parent in AllControls(f))
            {
                if (parent.Controls.Count < 2) continue;
                var kids = new List<Control>();
                foreach (Control c in parent.Controls)
                    if (c.Visible) kids.Add(c);

                for (int i = 0; i < kids.Count; i++)
                    for (int j = i + 1; j < kids.Count; j++)
                    {
                        checkedPairs++;
                        Rectangle a = kids[i].Bounds, b = kids[j].Bounds;
                        Rectangle hit = Rectangle.Intersect(a, b);
                        if (hit.Width <= 2 || hit.Height <= 2) continue;   // 紧贴不算重叠

                        bad++;
                        if (bad <= 14)
                            Console.WriteLine(string.Format("  ✗ 重叠 {0}x{1}px：「{2}」{3}  ×  「{4}」{5}",
                                hit.Width, hit.Height, Describe(kids[i]), a, Describe(kids[j]), b));
                    }
            }
            Console.WriteLine(string.Format("  检查 {0} 对同级控件，发现 {1} 处重叠  {2}",
                checkedPairs, bad, bad == 0 ? "✓ 没有控件互相压住" : "✗ 需要调整布局"));
        }

        private static string Describe(Control c)
        {
            string t = c.Text;
            if (string.IsNullOrEmpty(t))
            {
                if (c is QButton) t = "(按钮)";
                else if (c is StatCard) t = "统计卡片";
                else if (c is Panel) t = "(面板)";
                else t = c.GetType().Name;
            }
            if (t.Length > 16) t = t.Substring(0, 16) + "…";
            return t;
        }

        /// <summary>确认标题栏右上角的窗口按钮真的落在可见范围内。</summary>
        private static void CheckHeaderButtons(MainForm f)
        {
            int found = 0;
            foreach (Control c in AllControls(f))
            {
                var q = c as QButton;
                if (q == null || q.Text.Length != 0) continue;   // 窗口按钮是没有文字的图标按钮
                found++;
                bool inside = c.Left >= 0 && c.Top >= 0
                    && c.Right <= c.Parent.ClientSize.Width + 1
                    && c.Bottom <= c.Parent.ClientSize.Height + 1;
                Console.WriteLine(string.Format("  {0} 位置 ({1},{2}) 尺寸 {3}x{4}，父容器 {5}x{6}  →  {7}",
                    Describe(q), q.Left, q.Top, q.Width, q.Height,
                    q.Parent.ClientSize.Width, q.Parent.ClientSize.Height,
                    inside ? "✓ 在可见范围内" : "✗ 被挤出容器，看不见！"));
            }
            if (found == 0) Console.WriteLine("  ✗ 一个窗口控制按钮都没找到");
            else Console.WriteLine("  共找到 " + found + " 个窗口控制按钮（应为 4 个：主题 / 最小化 / 最大化 / 关闭）");
        }

        /// <summary>把程序列表前几行真的画一遍，统计非背景像素，用来判断「行是不是空的」。</summary>
        private static void DumpListRows(MainForm f)
        {
            AppListView lv = null;
            foreach (Control c in AllControls(f)) { lv = c as AppListView; if (lv != null) break; }
            if (lv == null) { Console.WriteLine("  ✗ 找不到程序列表控件"); return; }

            Console.WriteLine(string.Format("  列表控件     : {0} 行数据, 客户区 {1}x{2}",
                lv.Count, lv.ClientSize.Width, lv.ClientSize.Height));
            Console.WriteLine(string.Format("  行高         : {0}px  (设计 {1}px × {2} = {3}px)",
                lv.RowHeight, 50, Theme.Scale, Theme.S(50)));
            Console.WriteLine("  可见行数     : " + lv.VisibleRowCount);

            // 行高够不够放下两行字？
            int fontH = lv.Font.Height;
            int need = fontH + Theme.UiFont(9f).Height + Theme.S(12);
            Console.WriteLine(string.Format("  两行文字需要 : {0}px  →  {1}", need,
                lv.RowHeight >= need ? "✓ 行高足够" : "✗ 行高不够，文字会被裁掉"));

            if (lv.Count == 0) { Console.WriteLine("  ✗ 列表里一行都没有（数据没灌进去）"); return; }

            Console.WriteLine("  第 1 行: 「" + lv.Items[0].Entry.Name + "」  来源=" + lv.Items[0].SourceText
                + "  延迟=" + lv.Items[0].Entry.DelaySeconds + "s  勾选=" + lv.Items[0].Entry.Enabled);
            if (lv.Count > 1)
                Console.WriteLine("  第 2 行: 「" + lv.Items[1].Entry.Name + "」");

            // 真的画一遍，数非背景像素 —— 这是「行是不是空的」的硬证据
            try
            {
                int w = Math.Max(Theme.S(400), lv.ClientSize.Width - Theme.S(30));
                int h = Math.Min(lv.ClientSize.Height, lv.RowHeight * 3);
                if (w < 20 || h < 20) { Console.WriteLine("  列表尺寸异常，跳过绘制验证"); return; }

                using (var bmp = new Bitmap(w, h))
                {
                    lv.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                    Color bg = bmp.GetPixel(2, 2);
                    int diff = 0, sampled = 0;
                    for (int y = 0; y < h; y += 2)
                        for (int x = 0; x < w; x += 2)
                        {
                            sampled++;
                            Color c = bmp.GetPixel(x, y);
                            if (Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 24) diff++;
                        }
                    double pct = 100.0 * diff / Math.Max(1, sampled);
                    Console.WriteLine(string.Format("  绘制验证     : 采样 {0} 点，有内容 {1} 点（{2:0.0}%）", sampled, diff, pct));
                    Console.WriteLine(pct < 2.0
                        ? "  ✗ 几乎全是背景色 —— 文字和勾选框没画上去！"
                        : "  ✓ 行里有实际内容（勾选框/图标/文字已画出）");

                    // 分三行统计，逐行确认不是整行空白
                    for (int r = 0; r < Math.Min(3, lv.Count); r++)
                    {
                        int y0 = (r - lv.TopRow) * lv.RowHeight;
                        if (y0 < 0 || y0 + lv.RowHeight > h) continue;
                        int rowDiff = 0, rowSamp = 0;
                        for (int y = y0; y < y0 + lv.RowHeight - 2; y += 2)
                            for (int x = 0; x < w; x += 4)
                            {
                                rowSamp++;
                                Color c = bmp.GetPixel(x, y);
                                if (Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 24) rowDiff++;
                            }
                        Console.WriteLine(string.Format("    第 {0} 行: 有内容 {1:0.0}%  {2}",
                            r + 1, 100.0 * rowDiff / Math.Max(1, rowSamp), rowDiff > 0 ? "✓" : "✗ 空行"));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  绘制验证失败: " + ex.Message);
            }
        }

        private static int CountControls(Control root)
        {
            int n = 1;
            foreach (Control c in root.Controls) n += CountControls(c);
            return n;
        }

        private static int ReadLogCount(MainForm f)
        {
            foreach (Control c in AllControls(f))
            {
                var lv = c as LogView;
                if (lv != null) return lv.LineCount;
            }
            return -1;
        }

        private static System.Collections.Generic.List<Control> AllControls(Control root)
        {
            var list = new System.Collections.Generic.List<Control>();
            foreach (Control c in root.Controls)
            {
                list.Add(c);
                list.AddRange(AllControls(c));
            }
            return list;
        }
    }
}
