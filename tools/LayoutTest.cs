using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

/// <summary>
/// QuickLaunch 布局回归测试。
///
/// 为什么有它（对照 参考资料\注意事项.md 第 8/9/11/13 条）：
///   · 第一版用绝对坐标 + AutoScroll，右侧面板内容比面板高 → 底部统计卡片被裁掉、
///     文字互相压住（用户两次截图反馈"文字堆叠"）；
///   · 第二版把行高写死，150% 缩放下 ListView 只给 32px，两行文字被整个裁掉
///     → 用户看到"每一行都是空的"。
/// 这两类问题在 96 DPI 的开发机上看不出来，所以必须做成自动化断言。
///
/// 用法：
///   LayoutTest.exe &lt;QuickLaunch.exe 路径&gt;
///   LayoutTest.exe &lt;exe&gt; --size 1400x900       只测一个尺寸
///   LayoutTest.exe &lt;exe&gt; --scale 1.5           只测一个缩放比例
/// 退出码 0 = 全部通过。
/// </summary>
internal static class LayoutTest
{
    private static int _pass, _fail;

    private static readonly int[][] Sizes = new int[][]
    {
        new int[] { 1180, 820 },   // 最小可用
        new int[] { 1400, 900 },   // 默认
        new int[] { 1600, 1000 },  // 常用
        new int[] { 1920, 1080 },  // 大屏
        new int[] { 1100, 640 }    // 比最小还小，应被 MinimumSize 抬起来
    };

    private static readonly float[] Scales = new float[] { 1.0f, 1.25f, 1.5f };

    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }

        // 本工具会构造 MainForm 再关闭它，而关闭会触发"退出自动保存"。
        // 不隔离的话就会把用户真实的 config.json 覆盖成测试数据（真实踩过）。
        try
        {
            string iso = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QuickLaunchLayoutTest");
            System.IO.Directory.CreateDirectory(iso);
            Environment.SetEnvironmentVariable("QL_CFGDIR", iso);
        }
        catch { }

        string exe = args != null && args.Length > 0 ? args[0] : "QuickLaunch.exe";
        if (!System.IO.File.Exists(exe))
        {
            Console.WriteLine("[失败] 找不到目标 exe：" + exe);
            return 1;
        }

        string onlySize = null;
        float onlyScale = 0f;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--size" && i + 1 < args.Length) onlySize = args[++i];
            else if (args[i] == "--scale" && i + 1 < args.Length) float.TryParse(args[++i], out onlyScale);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Console.WriteLine("=== QuickLaunch 布局回归测试 ===");
        Console.WriteLine("目标：" + System.IO.Path.GetFullPath(exe));
        Console.WriteLine();

        Assembly asm = Assembly.UnsafeLoadFrom(System.IO.Path.GetFullPath(exe));
        Type formType = asm.GetType("QuickLaunch.MainForm");
        Type cfgType = asm.GetType("QuickLaunch.AppConfig");
        Type helperType = asm.GetType("QuickLaunch.Helper");
        Type themeType = asm.GetType("QuickLaunch.Theme");
        if (formType == null || cfgType == null)
        {
            Console.WriteLine("[失败] 目标 exe 里找不到 QuickLaunch.MainForm / AppConfig");
            return 1;
        }

        foreach (float scale in Scales)
        {
            if (onlyScale > 0f && Math.Abs(scale - onlyScale) > 0.001f) continue;

            // 通过环境变量注入缩放比例，程序里的 Theme.InitDpi() 会读它
            Environment.SetEnvironmentVariable("QL_DPI_SCALE",
                scale.ToString(System.Globalization.CultureInfo.InvariantCulture));

            Console.WriteLine("══════════ 缩放比例 " + (int)Math.Round(scale * 100) + "% ══════════");

            foreach (int[] size in Sizes)
            {
                string label = size[0] + "x" + size[1];
                if (onlySize != null && onlySize != label) continue;
                CheckOne(formType, cfgType, size[0], size[1], scale, label);
            }
            Console.WriteLine();
        }

        Environment.SetEnvironmentVariable("QL_DPI_SCALE", null);

        Console.WriteLine("──────────────────────────────");
        Console.WriteLine("通过 " + _pass + " 项，失败 " + _fail + " 项");
        return _fail == 0 ? 0 : 1;
    }

    private static void CheckOne(Type formType, Type cfgType, int w, int h, float scale, string label)
    {
        Console.WriteLine("--- " + label + " @ " + (int)Math.Round(scale * 100) + "% ---");

        Form form = null;
        try
        {
            object cfg = Activator.CreateInstance(cfgType);
            FillTestEntries(cfg, scale);                 // 灌测试数据，否则列表是空的、渲染检查没意义
            form = (Form)Activator.CreateInstance(formType, new object[] { cfg, 0L });

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-6000, -6000);     // 挪到屏幕外，别打扰用户
            form.ClientSize = new Size((int)Math.Round(w * scale), (int)Math.Round(h * scale));
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            Application.DoEvents();
            System.Threading.Thread.Sleep(120);
            Application.DoEvents();

            // ---------- 断言 1：同级控件不重叠
            int overlaps = 0;
            var details = new List<string>();
            foreach (Control parent in AllControls(form))
            {
                if (parent.Controls.Count < 2) continue;
                if (parent is FlowLayoutPanel) continue;          // 流式布局自动排列
                var kids = new List<Control>();
                foreach (Control c in parent.Controls) if (c.Visible) kids.Add(c);

                for (int i = 0; i < kids.Count; i++)
                    for (int j = i + 1; j < kids.Count; j++)
                    {
                        Rectangle a = kids[i].Bounds, b = kids[j].Bounds;
                        Rectangle hit = Rectangle.Intersect(a, b);
                        if (hit.Width <= 2 || hit.Height <= 2) continue;
                        if (a.Contains(b) || b.Contains(a)) continue;   // 容器套子控件不算
                        overlaps++;
                        if (details.Count < 5)
                            details.Add("      " + Describe(kids[i]) + a + "  ×  " + Describe(kids[j]) + b);
                    }
            }
            foreach (string d in details) Console.WriteLine(d);
            Check(label + " · 没有控件重叠", overlaps == 0, overlaps + " 处重叠");

            // ---------- 断言 2：程序列表行高够放下两行字
            Control list = FindByTypeName(form, "AppListView");
            if (list == null) { Check(label + " · 找到程序列表", false, "未找到 AppListView"); }
            else
            {
                PropertyInfo rowH = list.GetType().GetProperty("RowHeight");
                int rh = rowH == null ? 0 : (int)rowH.GetValue(list, null);
                // 行里画两行字：主字体 + 小字体，再加一点上下留白
                int need1 = list.Font.Height;
                int need2 = (int)Math.Round(need1 * 0.85);
                int need = need1 + need2 + (int)Math.Round(8 * scale);
                Check(label + " · 列表行高够放两行文字（≥" + need + "px）", rh >= need, rh + "px");
                Check(label + " · 列表可视高度够（≥5 行）", list.Height >= rh * 5, list.Height + "px / 行高" + rh);

                // ---------- 断言 3：列表真的画出了内容（不是空白行）
                CheckListPainted(list, label, rh, scale);
            }

            // ---------- 断言 4：右侧面板内容没被裁掉
            Control canvas = FindByTypeName(form, "_rightCanvas");
            if (canvas != null)
            {
                // 画布高度必须覆盖最底部控件的下边缘，否则说明高度算漏了、最后一张卡片会被裁
                int lowest = 0;
                foreach (Control c in canvas.Controls)
                {
                    int bottom = c.Bottom + c.Margin.Bottom;
                    if (bottom > lowest) lowest = bottom;
                }
                Check(label + " · 右侧画布高度覆盖所有内容",
                    canvas.Height >= lowest,
                    "画布 " + canvas.Height + " / 内容最低点 " + lowest);

                // 画布宽度不能超出面板，否则内容会被右边切掉（"最小化"变"最小"）
                int panelW = canvas.Parent == null ? canvas.Width : canvas.Parent.ClientSize.Width;
                Check(label + " · 右侧画布宽度不超出面板",
                    canvas.Width <= panelW + 2,
                    "画布 " + canvas.Width + " / 面板 " + panelW);

                // 装不下时必须有滚动能力（自绘滚动条通过 RightMaxScroll 判断）
                PropertyInfo maxScroll = form.GetType().GetProperty("RightMaxScroll",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                int maxScrollPx = maxScroll == null ? -1 : (int)maxScroll.GetValue(form, null);
                bool needScroll = canvas.Height > panelW * 0 + (canvas.Parent == null ? canvas.Height : canvas.Parent.ClientSize.Height);
                Check(label + " · 右侧内容超高时有滚动余量",
                    !needScroll || maxScrollPx > 0,
                    needScroll ? "需要滚动，RightMaxScroll=" + maxScrollPx : "内容放得下，无需滚动");
            }
            else
            {
                Check(label + " · 找到右侧面板画布", false, "未找到 _rightCanvas");
            }

            // ---------- 断言 5：底栏和日志面板没有被挤扁
            Control logView = FindByTypeName(form, "LogView");
            Check(label + " · 日志面板高度够（≥90px）", logView != null && logView.Height >= 90,
                logView == null ? "未找到" : logView.Height + "px");

            Control groupBar = FindByTypeName(form, "GroupBar");
            Check(label + " · 底栏分组按钮区够宽（≥200px）", groupBar != null && groupBar.Width >= 200,
                groupBar == null ? "未找到" : groupBar.Width + "px");

            // ---------- 断言 6：窗口控制按钮在可见范围内
            int checkedButtons = 0, offScreen = 0;
            foreach (Control c in AllControls(form))
            {
                if (!(c is Button) && c.GetType().Name != "QButton") continue;
                if (c.Text != null && c.Text.Length > 0) continue;     // 只查图标按钮
                checkedButtons++;
                if (c.Left < 0 || c.Top < 0 || c.Right > c.Parent.ClientSize.Width + 1
                    || c.Bottom > c.Parent.ClientSize.Height + 1) offScreen++;
            }
            Check(label + " · 窗口控制按钮都在可见范围内（应 4 个）",
                checkedButtons == 4 && offScreen == 0,
                "找到 " + checkedButtons + " 个，越界 " + offScreen + " 个");

            // ---------- 断言 7：没有被拉变形的按钮
            int stretched = 0, tiny = 0;
            foreach (Control c in AllControls(form))
            {
                if (c.GetType().Name != "QButton") continue;
                if (!c.Visible) continue;
                if (c.Height > (int)Math.Round(48 * scale)) stretched++;
                if (c.Width < (int)Math.Round(16 * scale) || c.Height < (int)Math.Round(16 * scale)) tiny++;
            }
            Check(label + " · 没有被纵向拉伸变形的按钮", stretched == 0, stretched + " 个变形");
            Check(label + " · 没有小到看不见的按钮", tiny == 0, tiny + " 个过小");
        }
        catch (Exception ex)
        {
            Check(label + " · 构造并显示窗口", false, ex.GetType().Name + " → " + ex.Message);
        }
        finally
        {
            if (form != null)
            {
                try { form.Close(); form.Dispose(); } catch { }
                Application.DoEvents();
            }
            Console.WriteLine();
        }
    }

    /// <summary>把列表前几行真的渲染到内存位图，数非背景像素，确认不是「空白行」。</summary>
    private static void CheckListPainted(Control list, string label, int rowH, float scale)
    {
        try
        {
            PropertyInfo countProp = list.GetType().GetProperty("Count");
            int count = countProp == null ? 0 : (int)countProp.GetValue(list, null);
            if (count == 0) { Check(label + " · 列表有数据", false, "0 行"); return; }

            int w = list.ClientSize.Width;
            int h = Math.Min(list.ClientSize.Height, rowH * 3);
            if (w < 40 || h < 20) { Check(label + " · 列表尺寸可用于渲染", false, w + "x" + h); return; }

            using (var bmp = new Bitmap(w, h))
            {
                list.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                Color bg = bmp.GetPixel(2, 2);
                int diff = 0, sampled = 0;
                for (int y = 0; y < h; y += 2)
                    for (int x = 0; x < w; x += 3)
                    {
                        sampled++;
                        Color c = bmp.GetPixel(x, y);
                        if (Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 24) diff++;
                    }
                double pct = 100.0 * diff / Math.Max(1, sampled);
                Check(label + " · 列表画出了实际内容（勾选框/图标/文字）",
                    pct >= 2.0, pct.ToString("0.0") + "% 非背景像素");
            }
        }
        catch (Exception ex)
        {
            Check(label + " · 列表渲染验证", false, ex.Message);
        }
    }

    /// <summary>
    /// 往配置里灌测试数据。没有数据的话列表是空的，
    /// 「列表画出实际内容」这条断言就退化成了"0 行也算通过"，失去意义。
    /// 用反射构造 AppEntry，避免测试工具编译时依赖主程序的类型。
    /// </summary>
    private static void FillTestEntries(object cfg, float scale)
    {
        try
        {
            Assembly asm = cfg.GetType().Assembly;
            Type entryType = asm.GetType("QuickLaunch.AppEntry");
            if (entryType == null) return;

            object listObj = cfg.GetType().GetField("Entries").GetValue(cfg);
            var add = listObj.GetType().GetMethod("Add");

            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string[] names = new string[]
            {
                "命令提示符", "记事本", "计算器", "画图", "任务管理器", "控制面板",
                "资源管理器", "写字板", "注册表编辑器", "系统配置", "磁盘清理", "事件查看器",
                "Service Host", "Very Long Program Name For Ellipsis Testing 测试省略号",
                "Chrome", "Edge", "WPS Office", "微信", "Visual Studio Code", "7-Zip"
            };
            string[] files = new string[]
            {
                "cmd.exe", "notepad.exe", "calc.exe", "mspaint.exe", "taskmgr.exe", "control.exe",
                "explorer.exe", "write.exe", "regedit.exe", "msconfig.exe", "cleanmgr.exe", "eventvwr.exe",
                "svchost.exe", "cmd.exe", "cmd.exe",
                "cmd.exe", "cmd.exe", "cmd.exe", "cmd.exe", "cmd.exe"
            };

            for (int i = 0; i < names.Length; i++)
            {
                object e = Activator.CreateInstance(entryType);
                entryType.GetProperty("Id").SetValue(e, (long)(i + 1), null);
                entryType.GetProperty("Name").SetValue(e, names[i], null);
                entryType.GetProperty("Target").SetValue(e,
                    System.IO.Path.Combine(sys, files[i % files.Length]), null);
                entryType.GetProperty("Source").SetValue(e,
                    (i % 3 == 0) ? "注册表" : (i % 3 == 1) ? "开始菜单" : "手动添加", null);
                entryType.GetProperty("DelaySeconds").SetValue(e, 2 + (i % 5), null);
                entryType.GetProperty("Group").SetValue(e, "daily", null);
                entryType.GetProperty("Enabled").SetValue(e, i % 4 != 3, null);   // 一部分不勾，验证灰字
                if (i == 13) entryType.GetProperty("Note").SetValue(e, "可能需要管理员权限", null);
                add.Invoke(listObj, new object[] { e });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  [警告] 测试数据填充失败：" + ex.Message);
        }
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        yield return root;
        foreach (Control c in root.Controls)
            foreach (Control d in AllControls(c))
                yield return d;
    }

    private static Control FindByTypeName(Control root, string typeName)
    {
        foreach (Control c in root.Controls)
        {
            if (c.GetType().Name == typeName || c.Name == typeName) return c;
            Control found = FindByTypeName(c, typeName);
            if (found != null) return found;
        }
        return null;
    }

    private static string Describe(Control c)
    {
        string text = c.Text == null ? "" : c.Text.Replace("\r", " ").Replace("\n", " ");
        if (text.Length > 16) text = text.Substring(0, 16) + "…";
        return c.GetType().Name + "(\"" + text + "\")";
    }

    private static void Check(string title, bool ok, string actual)
    {
        if (ok) { _pass++; Console.WriteLine("  [通过] " + title); }
        else { _fail++; Console.WriteLine("  [失败] " + title + "   → 实际：" + actual); }
    }
}
