using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

/// <summary>
/// 界面截图工具：把主窗体真的渲染成 PNG，用来肉眼核对。
///
/// 为什么有它（对照 参考资料\注意事项.md 第 14 条）：
///   "截图/探针工具比反复猜有用得多"。之前界面出问题，用户截图 → 我改 → 用户再截图，
///   一轮就是几十分钟。有了这个工具，改完立刻能自己看效果。
///
/// 用法：
///   Shot.exe &lt;QuickLaunch.exe&gt; &lt;输出目录&gt; [缩放比例...]
///   例：Shot.exe ..\dist\QuickLaunch.exe ..\shots 1.0 1.5
/// </summary>
internal static class Shot
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); } catch { }

        // 截图工具也会构造 MainForm 并关闭它（触发自动保存），必须和用户配置隔离
        try
        {
            string iso = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QuickLaunchShotTest");
            System.IO.Directory.CreateDirectory(iso);
            Environment.SetEnvironmentVariable("QL_CFGDIR", iso);
        }
        catch { }

        if (args.Length < 2)
        {
            Console.WriteLine("用法: Shot.exe <QuickLaunch.exe> <输出目录> [缩放比例...] [--empty]");
            Console.WriteLine("  --empty  截「空列表」状态（验证拖放区提示），不灌测试数据");
            return 1;
        }

        bool emptyState = false;
        var argList = new System.Collections.Generic.List<string>();
        foreach (string a in args)
        {
            if (a == "--empty") emptyState = true;
            else argList.Add(a);
        }
        args = argList.ToArray();

        string exe = System.IO.Path.GetFullPath(args[0]);
        string outDir = System.IO.Path.GetFullPath(args[1]);
        if (!System.IO.File.Exists(exe)) { Console.WriteLine("找不到 " + exe); return 1; }
        System.IO.Directory.CreateDirectory(outDir);

        float[] scales;
        if (args.Length > 2)
        {
            scales = new float[args.Length - 2];
            for (int i = 2; i < args.Length; i++)
                float.TryParse(args[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out scales[i - 2]);
        }
        else scales = new float[] { 1.0f, 1.5f };

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Assembly asm = Assembly.UnsafeLoadFrom(exe);
        Type formType = asm.GetType("QuickLaunch.MainForm");
        Type cfgType = asm.GetType("QuickLaunch.AppConfig");
        if (formType == null || cfgType == null) { Console.WriteLine("目标不是 QuickLaunch"); return 1; }

        // 渲染尺寸按**设计像素**给，缩放由程序自己的 DPI 逻辑处理
        int[][] sizes = new int[][]
        {
            new int[] { 1700, 1050 },   // 主视图（用户的窗口差不多这么大）
            new int[] { 1240, 860 }     // 最小窗口，验证窄屏下也不挤
        };

        int ok = 0;
        foreach (float scale in scales)
        {
            Environment.SetEnvironmentVariable("QL_DPI_SCALE",
                scale.ToString(System.Globalization.CultureInfo.InvariantCulture));

            foreach (int[] size in sizes)
            {
                string name = string.Format("ui-{0}%-{1}x{2}{3}.png",
                    (int)Math.Round(scale * 100), size[0], size[1],
                    emptyState ? "-empty" : "");
                string path = System.IO.Path.Combine(outDir, name);
                try
                {
                    object cfg = Activator.CreateInstance(cfgType);
                    if (!emptyState) FillEntries(cfg);   // --empty：不灌数据，截空列表的拖放区提示

                    using (Form f = (Form)Activator.CreateInstance(formType, new object[] { cfg, 0L }))
                    {
                        f.StartPosition = FormStartPosition.Manual;
                        f.Location = new Point(-8000, -8000);
                        // 传设计像素：缩放由程序自己的 DPI 逻辑处理，外面再乘一次就乘两遍了
                        f.ClientSize = new Size(size[0], size[1]);
                        f.Show();
                        Application.DoEvents();
                        f.PerformLayout();
                        // 让图标懒加载和日志刷新都跑几轮，截出来的才是真实效果
                        for (int i = 0; i < 12; i++)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(60);
                        }
                        f.Refresh();
                        Application.DoEvents();

                        using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                        {
                            f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                            bmp.Save(path, ImageFormat.Png);
                        }
                        f.Close();
                        Application.DoEvents();
                    }
                    Console.WriteLine("已生成 " + name + "  (" + new System.IO.FileInfo(path).Length / 1024 + " KB)");
                    ok++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("生成 " + name + " 失败: " + ex.GetType().Name + " → " + ex.Message);
                }
            }
        }

        Environment.SetEnvironmentVariable("QL_DPI_SCALE", null);
        Console.WriteLine("完成，共 " + ok + " 张 → " + outDir);
        return ok > 0 ? 0 : 1;
    }

    /// <summary>灌一批测试数据，让截图里的列表、统计卡片都有内容。</summary>
    private static void FillEntries(object cfg)
    {
        Assembly asm = cfg.GetType().Assembly;
        Type entryType = asm.GetType("QuickLaunch.AppEntry");
        if (entryType == null) return;

        object listObj = cfg.GetType().GetField("Entries").GetValue(cfg);
        var add = listObj.GetType().GetMethod("Add");

        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string syswow = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] names = new string[]
        {
            "Google Chrome", "微信", "WPS Office", "Visual Studio Code", "命令提示符",
            "记事本", "任务管理器", "EndNote", "draw.io", "Steam",
            "Arduino IDE 2.3.6", "Android Studio"
        };
        string[] targets = new string[]
        {
            System.IO.Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
            @"E:\WeChat\WeChat.exe",
            @"E:\WPS Office\ksolaunch.exe",
            @"E:\Microsoft VS Code\Code.exe",
            System.IO.Path.Combine(sys, "cmd.exe"),
            System.IO.Path.Combine(sys, "notepad.exe"),
            System.IO.Path.Combine(sys, "taskmgr.exe"),
            System.IO.Path.Combine(pf, @"EndNote 21\EndNote.exe"),
            @"E:\Diagrams\draw.io\draw.io.exe",
            @"E:\Steam\steam.exe",
            @"E:\arduino2\Arduino IDE\Arduino IDE.exe",
            @"E:\Android studio panda3\bin\studio64.exe"
        };
        string[] sources = new string[] { "注册表", "开始菜单", "手动添加", "注册表", "注册表", "注册表" };
        int[] delays = new int[] { 3, 3, 4, 5, 2, 2, 2, 6, 3, 8, 5, 6 };
        bool[] enabled = new bool[] { true, true, true, false, true, false, false, false, false, false, false, false };

        for (int i = 0; i < names.Length; i++)
        {
            object e = Activator.CreateInstance(entryType);
            entryType.GetProperty("Id").SetValue(e, (long)(i + 1), null);
            entryType.GetProperty("Name").SetValue(e, names[i], null);
            entryType.GetProperty("Target").SetValue(e, targets[i], null);
            entryType.GetProperty("Source").SetValue(e, sources[i % sources.Length], null);
            entryType.GetProperty("DelaySeconds").SetValue(e, delays[i], null);
            entryType.GetProperty("Group").SetValue(e, "daily", null);
            entryType.GetProperty("Enabled").SetValue(e, enabled[i], null);
            if (i == 6) entryType.GetProperty("Note").SetValue(e, "可能需要管理员权限", null);
            add.Invoke(listObj, new object[] { e });
        }
    }
}
