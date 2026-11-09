using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace QuickLaunch
{
    /// <summary>
    /// 自绘控件集合。用自绘而不是系统原生控件，是因为默认控件在深色主题下
    /// 边框和配色很难看，而且 UxTheme 的 hover 效果会闪。
    /// </summary>
    internal static class Theme
    {
        /// <summary>
        /// DPI 缩放系数。程序声明了 DPI 感知，Windows 不再替我们位图拉伸，
        /// 所以所有写死的像素值都要自己乘这个系数，否则在 125%/150% 缩放下会挤成一团。
        /// </summary>
        public static float Scale = 1f;

        /// <summary>仅供自动化测试：强制指定缩放比例（不改真实 DPI）。</summary>
        public static void ForceScaleForTesting(float scale)
        {
            if (scale < 0.75f) scale = 0.75f;
            if (scale > 3f) scale = 3f;
            Scale = scale;
            _fontName = PickFont();
        }

        /// <summary>把设计像素换算成当前 DPI 的实际像素。</summary>
        public static int S(int px)
        {
            if (Scale <= 1.001f) return px;
            return (int)Math.Round(px * Scale);
        }

        /// <summary>
        /// 启动时探测一次 DPI。环境变量 QL_DPI_SCALE 可强制指定缩放比例
        /// （如 1.25 / 1.5），用于在 96 DPI 机器上验证高缩放布局 —— 自动化测试专用。
        /// </summary>
        public static string InitDpi()
        {
            float forced = 0f;
            try
            {
                string env = Environment.GetEnvironmentVariable("QL_DPI_SCALE");
                if (!string.IsNullOrEmpty(env))
                {
                    float p;
                    if (float.TryParse(env, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out p) && p > 0f)
                        forced = p;
                }
            }
            catch { }

            if (forced > 0f)
            {
                Scale = forced;
                if (Scale < 0.75f) Scale = 0.75f;
                if (Scale > 3f) Scale = 3f;
                _fontName = PickFont();
                return string.Format("{0}%（由环境变量 QL_DPI_SCALE 注入）", Math.Round(Scale * 100));
            }

            float dpi = 96f;
            string how = "系统 DC";
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    dpi = g.DpiX;
            }
            catch { how = "探测失败，按 96"; }

            if (dpi <= 0) dpi = 96f;
            Scale = dpi / 96f;
            if (Scale < 1f) Scale = 1f;
            _fontName = PickFont();

            return string.Format("{0} DPI → 界面缩放 {1}%（{2}）", dpi, Math.Round(Scale * 100), how);
        }

        private static string _fontName = "";

        /// <summary>当前界面字体族（启动时探测一次，避免字体回退导致字宽估算错误）。</summary>
        public static string FontName
        {
            get
            {
                if (_fontName == null || _fontName.Length == 0) _fontName = PickFont();
                return _fontName;
            }
        }

        /// <summary>挑一个系统里真实存在的中文字体。</summary>
        private static string PickFont()
        {
            string[] candidates = new string[]
            {
                "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun"
            };
            foreach (string name in candidates)
            {
                try
                {
                    using (Font f = new Font(name, 9f))
                        if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return name;
                }
                catch { }
            }
            return "Microsoft YaHei";
        }

        public static readonly Color Bg = Color.FromArgb(0x25, 0x2A, 0x35);
        public static readonly Color Bg2 = Color.FromArgb(0x2E, 0x34, 0x40);
        public static readonly Color Bg3 = Color.FromArgb(0x33, 0x3A, 0x48);
        public static readonly Color Bg4 = Color.FromArgb(0x3D, 0x45, 0x57);
        public static readonly Color Line = Color.FromArgb(0x3B, 0x42, 0x50);
        public static readonly Color Line2 = Color.FromArgb(0x4A, 0x54, 0x68);
        public static readonly Color Tx = Color.FromArgb(0xEE, 0xF1, 0xF7);
        public static readonly Color Tx2 = Color.FromArgb(0xA8, 0xB1, 0xC2);
        public static readonly Color Tx3 = Color.FromArgb(0x7D, 0x87, 0x9A);
        public static readonly Color Ac = Color.FromArgb(0x4F, 0x7C, 0xFF);
        public static readonly Color Ac2 = Color.FromArgb(0x6B, 0x93, 0xFF);
        public static readonly Color Ok = Color.FromArgb(0x31, 0xD0, 0x7A);
        public static readonly Color Warn = Color.FromArgb(0xF5, 0xB9, 0x42);
        public static readonly Color Err = Color.FromArgb(0xFF, 0x5F, 0x6D);
        public static readonly Color RowHover = Color.FromArgb(0x2B, 0x31, 0x3D);
        public static readonly Color LogBg = Color.FromArgb(0x22, 0x26, 0x2F);

        /// <summary>优先用 Windows 11 的 Segoe Fluent Icons（比传统符号好看），取不到就退回 Segoe MDL2 / 普通字体。</summary>
        public static Font IconFont(float size)
        {
            try
            {
                using (var col = new InstalledFontCollection())
                {
                    bool fluent = false, mdl2 = false;
                    foreach (FontFamily f in col.Families)
                    {
                        if (f.Name == "Segoe Fluent Icons") { fluent = true; break; }
                        if (f.Name == "Segoe MDL2 Assets") mdl2 = true;
                    }
                    if (fluent) return new Font("Segoe Fluent Icons", size);
                    if (mdl2) return new Font("Segoe MDL2 Assets", size);
                }
            }
            catch { }
            return UiFont(size);
        }

        public static Font UiFont(float size)
        {
            return UiFont(size, FontStyle.Regular);
        }

        public static Font UiFont(float size, FontStyle style)
        {
            try { return new Font(FontName, size, style); }
            catch
            {
                try { return new Font("SimSun", size, style); }
                catch { return new Font(FontFamily.GenericSansSerif, size, style); }
            }
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color c, float width)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (var pen = new Pen(c, width))
                g.DrawPath(pen, p);
        }
    }

    // ------------------------------------------------------------------ 按钮

    internal sealed class QButton : Control
    {
        public enum Look { Normal, Primary, Ghost, Danger }

        private bool _hover, _down;
        public Look Style = Look.Normal;
        public string Glyph = "";
        public bool IsToggle;                                  // 开关按钮（带勾/叉图标）
        public Color TextColor = Theme.Tx;                     // 普通按钮的文字色
        public Color OnColor = Theme.Ok;                       // 开关：打开时
        public Color OffColor = Theme.Tx3;                     // 开关：关闭时

        public QButton(string text, Look look)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.UiFont(11f);
            Text = text;
            Style = look;
            Cursor = Cursors.Hand;
            Size = new Size(96, 30);
            BackColor = Theme.Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        public override Color ForeColor { get { return base.ForeColor; } set { base.ForeColor = value; Invalidate(); } }

        /// <summary>开关按钮用：true 表示打开。</summary>
        public bool TagIsOn
        {
            get { return Tag is bool && (bool)Tag; }
            set { Tag = value; }
        }

        public void SetOn(bool on)
        {
            IsToggle = true;
            TagIsOn = on;
            Glyph = on ? "\uE73E" : "\uE739";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, border, text;

            switch (Style)
            {
                case Look.Primary:
                    fill = _down ? Color.FromArgb(0x3E, 0x63, 0xD6) : (_hover ? Color.FromArgb(0x5F, 0x89, 0xFF) : Color.FromArgb(0x4F, 0x7C, 0xFF));
                    border = Color.FromArgb(0x5B, 0x86, 0xFF);
                    text = Color.White;
                    break;
                case Look.Ghost:
                    fill = _hover ? Theme.Bg4 : Color.Transparent;
                    border = _hover ? Theme.Line2 : Color.Transparent;
                    text = _hover ? Theme.Tx : Theme.Tx2;
                    break;
                case Look.Danger:
                    fill = _hover ? Color.FromArgb(0x6B, 0x2B, 0x33) : Theme.Bg3;
                    border = _hover ? Theme.Err : Theme.Line2;
                    text = _hover ? Color.White : Theme.Tx;
                    break;
                default:
                    fill = _down ? Theme.Bg2 : (_hover ? Color.FromArgb(0x3E, 0x46, 0x57) : Theme.Bg3);
                    border = _hover ? Color.FromArgb(0x55, 0x60, 0x74) : Theme.Line2;
                    text = Theme.Tx;
                    break;
            }

            if (!Enabled)
            {
                fill = Theme.Bg3;
                border = Theme.Line;
                text = Theme.Tx3;
            }

            if (fill.A > 0) Theme.FillRound(g, r, 7, fill);
            if (border.A > 0) Theme.DrawRound(g, r, 7, border, 1f);

            Color labelColor = text;
            if (Enabled)
            {
                if (IsToggle) labelColor = TagIsOn ? OnColor : OffColor;
                else if (Style == Look.Normal || Style == Look.Ghost) labelColor = TextColor;
            }

            // ------------------------------------------------------------
            // 图标必须用「图标字体」画，文字用界面字体画。
            // 之前把 Glyph + Text 拼成一个字符串、统一用界面字体（微软雅黑）画，
            // 结果 Segoe MDL2 的私有区字符在雅黑里没有字形 → 画出来是空白。
            // 右上角那三个窗口按钮只有图标没有文字，于是整组"消失"了。
            // ------------------------------------------------------------
            using (var gf = Theme.IconFont(9.5f))
            {
                int glyphW = 0, gap = 0;
                if (!string.IsNullOrEmpty(Glyph))
                {
                    glyphW = TextRenderer.MeasureText(Glyph, gf).Width;
                    if (!string.IsNullOrEmpty(Text)) gap = Theme.S(7);
                }
                int textW = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
                int totalW = glyphW + gap + textW;

                int x = r.X + Math.Max(0, (r.Width - totalW) / 2);
                if (glyphW > 0)
                {
                    // 图标区多给 2px 余量，避免个别字形被裁边
                    TextRenderer.DrawText(g, Glyph, gf,
                        new Rectangle(x, r.Y, glyphW + Theme.S(2), r.Height), labelColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    x += glyphW + gap;
                }
                if (textW > 0)
                {
                    TextRenderer.DrawText(g, Text, Font,
                        new Rectangle(x, r.Y, Math.Max(Theme.S(8), r.Right - x - Theme.S(2)), r.Height),
                        labelColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }
            }
        }

        /// <summary>
        /// 按"图标字体 + 界面字体"分别量出来的首选宽度。
        /// 外部（工具栏/底栏）用它来定按钮宽度，否则按单一字体会量偏导致文字被截。
        /// </summary>
        public int MeasureContentWidth()
        {
            int w = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
            if (!string.IsNullOrEmpty(Glyph))
            {
                using (var gf = Theme.IconFont(9.5f))
                    w += TextRenderer.MeasureText(Glyph, gf).Width;
                if (!string.IsNullOrEmpty(Text)) w += Theme.S(7);
            }
            return w;
        }
    }

    // -------------------------------------------------------------- 日志视图

    internal sealed class LogView : Control
    {
        private readonly List<Logging.LogItem> _all = new List<Logging.LogItem>();
        private LogLevel? _filter;
        private int _scroll, _rowH = Theme.S(19);

        public LogView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = new Font("Consolas", 9.5f);
            BackColor = Theme.LogBg;
        }

        public int LineCount { get { return _all.Count; } }

        public void SetFilter(LogLevel? lv) { _filter = lv; Clamp(); Invalidate(); }

        public void Clear() { _all.Clear(); _scroll = 0; Invalidate(); }

        public void AddRange(List<Logging.LogItem> items)
        {
            if (items == null || items.Count == 0) return;
            bool atBottom = _scroll >= Math.Max(0, VisibleRows - 1) - 1;

            foreach (Logging.LogItem it in items)
                _all.Add(it);
            if (_all.Count > 1500) _all.RemoveRange(0, _all.Count - 1500);

            Clamp();
            if (atBottom) _scroll = Math.Max(0, VisibleRows - ClientSize.Height / _rowH);
            Invalidate();
        }

        private int VisibleRows
        {
            get { return Math.Max(1, ClientSize.Height / _rowH); }
        }

        private List<Logging.LogItem> Filtered()
        {
            if (!_filter.HasValue) return _all;
            var list = new List<Logging.LogItem>();
            foreach (Logging.LogItem it in _all)
                if (it.Level == _filter.Value) list.Add(it);
            return list;
        }

        private void Clamp()
        {
            int max = Math.Max(0, Filtered().Count - VisibleRows);
            if (_scroll > max) _scroll = max;
            if (_scroll < 0) _scroll = 0;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int delta = -e.Delta / 120;
            _scroll += delta * 3;
            Clamp();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnResize(EventArgs e)
        {
            Clamp();
            base.OnResize(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.LogBg);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            List<Logging.LogItem> rows = Filtered();
            int y = Theme.S(2);
            for (int i = _scroll; i < rows.Count && y < ClientSize.Height; i++, y += _rowH)
            {
                Logging.LogItem it = rows[i];
                Color c = LevelColor(it.Level);

                string ts = it.Time.ToString("HH:mm:ss.fff");
                string lv = LevelText(it.Level);

                TextRenderer.DrawText(g, ts, Font, new Point(Theme.S(6), y), Color.FromArgb(0x6D, 0x76, 0x88), TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, lv, Font, new Point(Theme.S(88), y), c, TextFormatFlags.NoPrefix);

                var msgRect = new Rectangle(Theme.S(140), y, Math.Max(Theme.S(10), ClientSize.Width - Theme.S(146)), _rowH);
                TextRenderer.DrawText(g, it.Message, Font, msgRect, Color.FromArgb(0xC4, 0xCC, 0xDA),
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }

            if (rows.Count == 0)
                TextRenderer.DrawText(g, "（暂无日志）", Font, new Point(Theme.S(8), Theme.S(4)), Theme.Tx3, TextFormatFlags.NoPrefix);

            // 滚动位置提示
            if (rows.Count > VisibleRows)
            {
                string pos = (_scroll + 1) + "-" + Math.Min(rows.Count, _scroll + VisibleRows) + " / " + rows.Count;
                var sz = TextRenderer.MeasureText(pos, Font);
                TextRenderer.DrawText(g, pos, Font,
                    new Point(ClientSize.Width - sz.Width - Theme.S(8), ClientSize.Height - _rowH - Theme.S(2)),
                    Color.FromArgb(0x5A, 0x63, 0x74), TextFormatFlags.NoPrefix);
            }
        }

        public static Color LevelColor(LogLevel lv)
        {
            switch (lv)
            {
                case LogLevel.OK: return Theme.Ok;
                case LogLevel.WARN: return Theme.Warn;
                case LogLevel.ERR: return Theme.Err;
                case LogLevel.WAIT: return Color.FromArgb(0x8B, 0x95, 0xAA);
                default: return Theme.Ac2;
            }
        }

        public static string LevelText(LogLevel lv)
        {
            switch (lv)
            {
                case LogLevel.OK: return "OK";
                case LogLevel.WARN: return "WARN";
                case LogLevel.ERR: return "ERR";
                case LogLevel.WAIT: return "WAIT";
                default: return "INFO";
            }
        }
    }

    // --------------------------------------------------------- 分组行自绘控件

    internal sealed class GroupList : Control
    {
        public sealed class Row
        {
            public string Key = "";
            public string Icon = "";
            public string Text = "";
            public int Count;
            public bool ShowPlay;
            public bool Selected;
        }

        private readonly List<Row> _rows = new List<Row>();
        private int _hover = -1, _playHover = -1;
        private readonly int _rowH = Theme.S(35);

        public event EventHandler SelectionChanged;
        public event EventHandler<Row> PlayClicked;

        public GroupList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.UiFont(10.5f);
            BackColor = Theme.Bg2;
        }

        public string SelectedKey
        {
            get
            {
                foreach (Row r in _rows) if (r.Selected) return r.Key;
                return "";
            }
        }

        public void SetRows(List<Row> rows)
        {
            _rows.Clear();
            _rows.AddRange(rows);
            _hover = -1;
            Invalidate();
        }

        private Row RowAt(int y)
        {
            int i = y / _rowH;
            if (i < 0 || i >= _rows.Count) return null;
            return _rows[i];
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Row r = RowAt(e.Y);
            int idx = r == null ? -1 : _rows.IndexOf(r);
            int play = -1;
            if (r != null && r.ShowPlay && e.X >= ClientSize.Width - 32)
                play = idx;

            if (idx != _hover || play != _playHover)
            {
                _hover = idx;
                _playHover = play;
                Cursor = play >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; _playHover = -1; Cursor = Cursors.Default; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Row r = RowAt(e.Y);
            if (r != null)
            {
                if (r.ShowPlay && e.X >= ClientSize.Width - Theme.S(32))
                {
                    if (PlayClicked != null) PlayClicked(this, r);
                    return;
                }
                foreach (Row x in _rows) x.Selected = false;
                r.Selected = true;
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                var rect = new Rectangle(Theme.S(6), i * _rowH + Theme.S(2), ClientSize.Width - Theme.S(12), _rowH - Theme.S(3));

                if (r.Selected)
                    Theme.FillRound(g, rect, 7, Color.FromArgb(0x46, 0x5A, 0x7E));
                else if (i == _hover)
                    Theme.FillRound(g, rect, 7, Theme.Bg3);

                if (r.Selected)
                {
                    var bar = new Rectangle(0, rect.Y + Theme.S(5), Theme.S(3), rect.Height - Theme.S(10));
                    Theme.FillRound(g, bar, 1, Theme.Ac);
                }

                int x = rect.X + Theme.S(12);
                Color fg = r.Selected ? Theme.Tx : Theme.Tx2;

                if (Helper.HasText(r.Icon))
                {
                    using (var f = Theme.IconFont(10.5f))
                        TextRenderer.DrawText(g, r.Icon, f, new Point(x, rect.Y + Theme.S(8)), r.Selected ? Theme.Ac2 : Theme.Tx3);
                    x += Theme.S(20);
                }

                var textRect = new Rectangle(x, rect.Y, rect.Width - (x - rect.X) - Theme.S(34), rect.Height);
                TextRenderer.DrawText(g, r.Text, Font, textRect, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                string cnt = r.Count.ToString();
                int cntW = TextRenderer.MeasureText(cnt, Font).Width;

                if (r.ShowPlay)
                {
                    var pb = new Rectangle(rect.Right - Theme.S(28), rect.Y + Theme.S(4), Theme.S(24), rect.Height - Theme.S(8));
                    if (i == _playHover) Theme.FillRound(g, pb, 6, Color.FromArgb(0x55, 0x8B, 0x6E));
                    using (var f = Theme.IconFont(9.5f))
                        TextRenderer.DrawText(g, "\uE102", f, pb,
                            i == _playHover ? Theme.Ok : Theme.Tx3,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else
                {
                    TextRenderer.DrawText(g, cnt, Font,
                        new Rectangle(rect.Right - cntW - Theme.S(12), rect.Y, cntW + Theme.S(4), rect.Height),
                        r.Selected ? Theme.Ac2 : Theme.Tx3,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }
    }

    // ------------------------------------------------------------- 统计小卡片

    internal sealed class StatCard : Control
    {
        public string Caption = "";
        public string Value = "";
        public Color ValueColor = Theme.Tx;

        public StatCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.UiFont(11f);
            BackColor = Theme.Bg2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.S(8), Theme.Bg3);

            TextRenderer.DrawText(g, Caption, Font, new Rectangle(Theme.S(10), Theme.S(5), Width - Theme.S(16), Theme.S(16)), Theme.Tx3,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            using (var vf = Theme.UiFont(12.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, Value, vf, new Rectangle(Theme.S(10), Theme.S(20), Width - Theme.S(16), Height - Theme.S(22)), ValueColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // ------------------------------------------------------- 底栏分组启动按钮

    internal sealed class GroupBar : Control
    {
        public sealed class Item
        {
            public string Key = "";
            public string Text = "";
            public int Count;
        }

        private readonly List<Item> _items = new List<Item>();
        private readonly List<Rectangle> _rects = new List<Rectangle>();
        private int _hover = -1;

        public event EventHandler<Item> ItemClicked;

        public GroupBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.UiFont(11f);
            BackColor = Theme.Bg2;
        }

        public void SetItems(List<Item> items)
        {
            _items.Clear();
            _items.AddRange(items);
            _hover = -1;
            Invalidate();
        }

        private new void Layout()
        {
            _rects.Clear();
            int x = 0;
            foreach (Item it in _items)
            {
                string label = it.Text + "  " + it.Count;
                int w = TextRenderer.MeasureText(label, Font).Width + Theme.S(34);
                if (w < Theme.S(92)) w = Theme.S(92);
                _rects.Add(new Rectangle(x, 0, w, Theme.S(24)));
                x += w + Theme.S(8);
            }
        }

        protected override void OnResize(EventArgs e) { Layout(); base.OnResize(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < _rects.Count; i++)
                if (_rects[i].Contains(e.Location)) { h = i; break; }
            if (h != _hover)
            {
                _hover = h;
                Cursor = h >= 0 && _items[h].Count > 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; Cursor = Cursors.Default; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < _rects.Count; i++)
            {
                if (_rects[i].Contains(e.Location))
                {
                    if (_items[i].Count > 0 && ItemClicked != null) ItemClicked(this, _items[i]);
                    return;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            if (_rects.Count != _items.Count) Layout();

            for (int i = 0; i < _items.Count && i < _rects.Count; i++)
            {
                Item it = _items[i];
                Rectangle r = _rects[i];
                bool usable = it.Count > 0;
                bool hot = usable && i == _hover;

                Color fill = hot ? Color.FromArgb(0x33, 0x5A, 0x48) : Theme.Bg3;
                Color border = hot ? Theme.Ok : Theme.Line2;
                if (!usable) { fill = Theme.Bg2; border = Theme.Line; }

                Theme.FillRound(g, r, 12, fill);
                Theme.DrawRound(g, r, 12, border, 1f);

                var dot = new Rectangle(r.X + Theme.S(11), r.Y + r.Height / 2 - Theme.S(3), Theme.S(6), Theme.S(6));
                using (var b = new SolidBrush(usable ? Theme.Ok : Theme.Tx3))
                    g.FillEllipse(b, dot);

                string label = it.Text + "  " + it.Count;
                TextRenderer.DrawText(g, label, Font,
                    new Rectangle(r.X + Theme.S(22), r.Y, r.Width - Theme.S(26), r.Height),
                    usable ? (hot ? Color.White : Theme.Tx) : Theme.Tx3,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    // ------------------------------------------------------ 程序列表（全自绘）

    /// <summary>
    /// 程序列表。刻意不用 ListView：ListView 的行高由系统按「小图标列表高度」推算，
    /// 在 150% 缩放下只给到 32px，而两行文字（名称 + 路径）需要 56px+，文字会被整个裁掉
    /// —— 表现就是「每一行都是空的」。自己画就完全没有这个坑。
    /// </summary>
    internal sealed class AppListView : Control
    {
        internal sealed class Item
        {
            public AppEntry Entry;
            public string SourceText = "";
            public Color SourceColor = Theme.Tx3;
            public int Status;              // 0 空闲 1 启动中 2 成功 3 失败 4 跳过
            public string Message = "";
        }

        private readonly List<Item> _items = new List<Item>();
        private readonly VScrollBar _vs;
        private int _hover = -1;
        private int _hotAction = -1;
        private bool _iconPending;
        private int _iconDrawnThisPass, _repaintBurst;

        public event EventHandler<Item> CheckToggled;
        public event EventHandler<Item> ActionLaunch;
        public event EventHandler<Item> ActionFolder;
        public event EventHandler<Item> ActionRemove;
        public event EventHandler<Item> ItemSelected;
        public event EventHandler<Item> DelayEditRequested;
        /// <summary>列表为空时双击空白处（用来打开"添加程序"对话框）。</summary>
        public event EventHandler EmptyAreaActivated;

        public ImageList IconList;
        private readonly Dictionary<string, int> _iconIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public AppListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.UiFont(10.5f);
            BackColor = Theme.Bg;
            TabStop = true;

            _vs = new VScrollBar();
            _vs.SmallChange = 1;
            _vs.ValueChanged += delegate { Invalidate(); };
            _vs.Visible = false;   // 只用它保存滚动位置；外观由我们自己画（原生滚动条是白色主题，在深色界面上很突兀）
        }

        /// <summary>滚动条只在需要时占用右边缘这么宽。</summary>
        private int ScrollBarW { get { return Theme.S(8); } }
        private bool NeedsScroll { get { return _items.Count > VisibleRowCount; } }

        private Rectangle ScrollTrack()
        {
            int x = ClientSize.Width - ScrollBarW - Theme.S(2);
            return new Rectangle(x, Theme.S(4), ScrollBarW, Math.Max(1, ClientSize.Height - Theme.S(8)));
        }

        /// <summary>滑块矩形。位置由 _vs.Value 换算，和滚动逻辑共用同一个状态。</summary>
        private Rectangle ScrollThumb()
        {
            if (!NeedsScroll) return Rectangle.Empty;
            Rectangle track = ScrollTrack();
            int total = _items.Count;
            int visible = VisibleRowCount;
            int thumbH = (int)Math.Max(Theme.S(28), (long)track.Height * visible / Math.Max(1, total));
            if (thumbH > track.Height) thumbH = track.Height;
            int maxTop = Math.Max(1, total - visible);
            int range = track.Height - thumbH;
            int pos = (int)((long)range * Math.Min(_vs.Value, maxTop) / maxTop);
            return new Rectangle(track.X, track.Y + pos, ScrollBarW, thumbH);
        }

        /// <summary>画一条细的自绘滚动条（深色主题，不占布局宽度）。</summary>
        private void DrawScrollBar(Graphics g)
        {
            if (!NeedsScroll) return;
            Rectangle track = ScrollTrack();
            Theme.FillRound(g, track, ScrollBarW / 2, Color.FromArgb(0x28, 0x2E, 0x3A));

            Rectangle thumb = ScrollThumb();
            bool hot = _scrollDragging || _scrollHot;
            Theme.FillRound(g, thumb, ScrollBarW / 2,
                hot ? Color.FromArgb(0x6B, 0x77, 0x8C) : Color.FromArgb(0x4A, 0x54, 0x68));
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            // 注意：_vs 刻意不加入 Controls —— 它是隐藏的状态容器，
            // 加进去会以白色原生外观显示在右边（这正是之前那条"白色竖条"的由来）
        }

        public int RowHeight { get { return Theme.S(50); } }
        internal int VisibleRowCount { get { return Math.Max(1, ClientSize.Height / Math.Max(1, RowHeight)); } }
        public int Count { get { return _items.Count; } }
        public IList<Item> Items { get { return _items; } }
        public int TopRow { get { return _vs.Value; } }

        public void SetItems(List<Item> items)
        {
            _items.Clear();
            _items.AddRange(items);
            ResetIconCache();
            _hover = -1;
            _hotAction = -1;
            SyncScroll();
            Invalidate();
        }

        public void SetItemStatus(AppEntry e, int status, string message)
        {
            foreach (Item it in _items)
            {
                if (it.Entry != e) continue;
                it.Status = status;
                it.Message = message == null ? "" : message;
                Invalidate();
                return;
            }
        }

        public void ResetIconCache()
        {
            _iconIndex.Clear();
            _iconDrawnThisPass = 0;
            _iconPending = false;
            _repaintBurst = 0;
        }

        public Rectangle RowBounds(int index)
        {
            return new Rectangle(0, (index - TopRow) * RowHeight, ClientSize.Width, RowHeight);
        }

        public Rectangle CheckRect(int index)
        {
            Rectangle r = RowBounds(index);
            int sz = Theme.S(20);
            return new Rectangle(Theme.S(14), r.Y + r.Height / 2 - sz / 2, sz, sz);
        }

        /// <summary>行的可用右边界（滚动条可见时给它让出位置）。</summary>
        private int RowRight
        {
            get { return ClientSize.Width - (NeedsScroll ? ScrollBarW + Theme.S(6) : 0); }
        }

        /// <summary>三个操作按钮的位置（绘制与点击判定共用同一套计算）。</summary>
        public Rectangle[] ActionRects(int index)
        {
            Rectangle r = RowBounds(index);
            int x = RowRight - Theme.S(106);
            var rects = new Rectangle[3];
            for (int i = 0; i < 3; i++)
                rects[i] = new Rectangle(x + i * Theme.S(30), r.Y + r.Height / 2 - Theme.S(13), Theme.S(26), Theme.S(26));
            return rects;
        }

        private void SyncScroll()
        {
            int visible = VisibleRowCount;
            int max = Math.Max(0, _items.Count - visible);
            _vs.Minimum = 0;
            _vs.Maximum = Math.Max(0, _items.Count - 1);
            _vs.LargeChange = Math.Max(1, visible);
            _vs.Enabled = _items.Count > visible;
            if (_vs.Value > max) _vs.Value = max;
        }

        protected override void OnResize(EventArgs e)
        {
            SyncScroll();
            base.OnResize(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int max = Math.Max(0, _items.Count - VisibleRowCount);
            int v = Math.Min(max, Math.Max(0, _vs.Value - e.Delta / 120 * 3));
            if (v != _vs.Value) { _vs.Value = v; Invalidate(); }
            base.OnMouseWheel(e);
        }

        public int IndexAt(Point p)
        {
            int i = TopRow + p.Y / Math.Max(1, RowHeight);
            return (i >= 0 && i < _items.Count) ? i : -1;
        }

        // ---- 自绘滚动条的拖动状态
        private bool _scrollDragging;
        private bool _scrollHot;
        private int _dragStartY;
        private int _dragStartPos;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            // 滚动条拖动优先
            if (_scrollDragging)
            {
                Rectangle track = ScrollTrack();
                int thumbH = ScrollThumb().Height;
                int range = Math.Max(1, track.Height - thumbH);
                int maxTop = Math.Max(1, _items.Count - VisibleRowCount);
                int pos = _dragStartPos + (e.Y - _dragStartY);
                if (pos < 0) pos = 0;
                if (pos > range) pos = range;
                int v = (int)Math.Round((double)pos * maxTop / range);
                if (v != _vs.Value) { _vs.Value = v; Invalidate(); }
                return;
            }

            bool hot = NeedsScroll && ScrollThumb().Contains(e.Location);
            if (hot != _scrollHot) { _scrollHot = hot; Invalidate(); }

            int idx = IndexAt(e.Location);
            int act = -1;
            if (idx >= 0)
            {
                Rectangle[] rects = ActionRects(idx);
                for (int i = 0; i < rects.Length; i++)
                    if (rects[i].Contains(e.Location)) { act = i; break; }
            }
            if (idx != _hover || act != _hotAction)
            {
                _hover = idx;
                _hotAction = act;
                Cursor = act >= 0 || hot ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();

            // 自绘滚动条：拖动滑块 / 点空白轨道翻页
            if (NeedsScroll && ScrollTrack().Contains(e.Location))
            {
                Rectangle thumb = ScrollThumb();
                if (thumb.Contains(e.Location))
                {
                    _scrollDragging = true;
                    _dragStartY = e.Y;
                    _dragStartPos = thumb.Y - ScrollTrack().Y;
                    Capture = true;
                    Invalidate();
                }
                else
                {
                    // 点击轨道：往上/往下翻一页
                    int page = Math.Max(1, VisibleRowCount - 1);
                    int max = Math.Max(0, _items.Count - VisibleRowCount);
                    int v = e.Y < thumb.Y ? _vs.Value - page : _vs.Value + page;
                    _vs.Value = Math.Min(max, Math.Max(0, v));
                    Invalidate();
                }
                return;
            }

            int idx = IndexAt(e.Location);
            if (idx < 0) return;
            Item it = _items[idx];

            Rectangle[] rects = ActionRects(idx);
            if (rects[0].Contains(e.Location)) { if (ActionLaunch != null) ActionLaunch(this, it); return; }
            if (rects[1].Contains(e.Location)) { if (ActionFolder != null) ActionFolder(this, it); return; }
            if (rects[2].Contains(e.Location)) { if (ActionRemove != null) ActionRemove(this, it); return; }
            if (CheckRect(idx).Contains(e.Location)) { if (CheckToggled != null) CheckToggled(this, it); return; }

            if (ItemSelected != null) ItemSelected(this, it);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_scrollDragging)
            {
                _scrollDragging = false;
                Capture = false;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            bool dirty = _scrollHot || _hover >= 0 || _hotAction >= 0;
            _scrollHot = false;
            _hover = -1;
            _hotAction = -1;
            Cursor = Cursors.Default;
            if (dirty) Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int idx = IndexAt(e.Location);
            if (idx < 0)
            {
                if (EmptyAreaActivated != null) EmptyAreaActivated(this, EventArgs.Empty);
                return;
            }
            Item it = _items[idx];

            Rectangle[] rects = ActionRects(idx);
            for (int i = 0; i < 3; i++) if (rects[i].Contains(e.Location)) return;
            if (CheckRect(idx).Contains(e.Location)) return;

            // 双击延迟徽标改延迟，双击别处切换勾选
            if (e.X > ClientSize.Width - Theme.S(210) && e.X < ClientSize.Width - Theme.S(120))
            {
                if (DelayEditRequested != null) DelayEditRequested(this, it);
            }
            else
            {
                if (CheckToggled != null) CheckToggled(this, it);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Font fName = Theme.UiFont(10.5f);
            Font fSmall = Theme.UiFont(9f);
            Font fMono = new Font("Consolas", 8.5f);

            try
            {
                int rh = RowHeight;
                int first = TopRow;
                int last = Math.Min(_items.Count, first + VisibleRowCount + 1);

                for (int i = first; i < last; i++)
                {
                    Item it = _items[i];
                    var row = new Rectangle(0, (i - first) * rh, ClientSize.Width, rh);

                    Color bg = Theme.Bg;
                    if (it.Status == 1) bg = Color.FromArgb(0x2A, 0x3B, 0x33);
                    else if (it.Status == 2) bg = Color.FromArgb(0x27, 0x38, 0x33);
                    else if (it.Status == 4) bg = Color.FromArgb(0x2B, 0x30, 0x3A);
                    else if (it.Status == 3) bg = Color.FromArgb(0x3A, 0x2A, 0x2E);
                    else if (i == _hover) bg = Theme.RowHover;

                    using (var b = new SolidBrush(bg)) g.FillRectangle(b, row);
                    using (var p = new Pen(Color.FromArgb(0x33, 0x3A, 0x48)))
                        g.DrawLine(p, 0, row.Y + rh - 1, ClientSize.Width, row.Y + rh - 1);

                    if (it.Status == 1)
                        using (var b = new SolidBrush(Theme.Ok))
                            g.FillRectangle(b, new Rectangle(0, row.Y, Theme.S(3), rh - 1));

                    DrawRow(g, it, i, row, fName, fSmall, fMono);
                }

                if (_items.Count == 0) DrawEmptyState(g);

                DrawScrollBar(g);   // 自绘细滚动条（原生滚动条是白色主题，跟深色界面不搭）
            }
            finally
            {
                fName.Dispose();
                fSmall.Dispose();
                fMono.Dispose();
            }
        }

        private void DrawRow(Graphics g, Item it, int index, Rectangle row, Font fName, Font fSmall, Font fMono)
        {
            AppEntry e = it.Entry;

            // ---- 勾选框
            Rectangle box = CheckRect(index);
            if (e.Enabled)
            {
                Theme.FillRound(g, box, Theme.S(4), Theme.Ac);
                using (var p = new Pen(Color.White, Math.Max(1.7f, 1.7f * Theme.Scale)))
                    g.DrawLines(p, new Point[]
                    {
                        new Point(box.X + Theme.S(4), box.Y + Theme.S(9)),
                        new Point(box.X + Theme.S(7), box.Y + Theme.S(13)),
                        new Point(box.X + Theme.S(14), box.Y + Theme.S(5))
                    });
            }
            else
            {
                Theme.FillRound(g, box, Theme.S(4), Theme.Bg3);
                Theme.DrawRound(g, box, Theme.S(4), Theme.Line2, 1.4f);
            }

            // ---- 图标（懒加载）
            int isz = Theme.S(22);
            var iconBox = new Rectangle(box.Right + Theme.S(12), row.Y + row.Height / 2 - isz / 2, isz, isz);
            int iconIdx = EnsureIcon(e.Target);
            bool drew = false;
            if (iconIdx >= 0 && IconList != null && iconIdx < IconList.Images.Count)
            {
                try { IconList.Draw(g, iconBox.Location, iconIdx); drew = true; }
                catch { }
            }
            if (!drew)
            {
                Theme.FillRound(g, iconBox, Theme.S(5), AppIcon.FallbackColor(e.Name));
                using (var f = Theme.UiFont(9.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, AppIcon.Initial(e.Name), f, iconBox, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            // ---- 第一行：名称 + 运行状态
            int textX = iconBox.Right + Theme.S(12);
            int textW = RowRight - textX - Theme.S(214);
            if (textW < Theme.S(90)) textW = Theme.S(90);

            string name = e.Name;
            if (it.Message.Length > 0) name += "        " + it.Message;
            Color nameColor = e.Enabled ? Theme.Tx : Theme.Tx3;
            if (it.Status == 3) nameColor = Theme.Err;
            else if (it.Status == 2) nameColor = Theme.Ok;

            TextRenderer.DrawText(g, name, fName, new Rectangle(textX, row.Y + Theme.S(4), textW, Theme.S(22)),
                nameColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // ---- 第二行：来源 · 路径
            string sub = it.SourceText + "   ·   " + e.Target;
            Color subColor = Theme.Tx3;
            if (Helper.HasText(e.Note) && e.Note.IndexOf("管理员", StringComparison.Ordinal) >= 0)
                subColor = Theme.Warn;
            TextRenderer.DrawText(g, sub, fMono, new Rectangle(textX, row.Y + Theme.S(25), textW, Theme.S(18)),
                subColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // ---- 延迟徽标
            string delayText = e.DelaySeconds + " 秒";
            Size ds = TextRenderer.MeasureText(delayText, fSmall);
            var delayBox = new Rectangle(RowRight - Theme.S(198), row.Y + row.Height / 2 - Theme.S(13),
                Math.Max(Theme.S(58), ds.Width + Theme.S(22)), Theme.S(26));
            Theme.FillRound(g, delayBox, Theme.S(6), Theme.Bg3);
            Theme.DrawRound(g, delayBox, Theme.S(6), Theme.Line2, 1f);
            TextRenderer.DrawText(g, delayText, fSmall, delayBox,
                e.DelaySeconds == 0 ? Theme.Tx3 : Theme.Tx,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // ---- 三个操作按钮
            Rectangle[] rects = ActionRects(index);
            string[] glyphs = new string[] { "\uE768", "\uE8B7", "\uE711" };
            Color[] normal = new Color[] { Theme.Ac2, Theme.Tx3, Theme.Tx3 };
            Color[] hot = new Color[] { Theme.Ok, Theme.Tx, Theme.Err };
            for (int i = 0; i < 3; i++)
            {
                bool isHot = index == _hover && i == _hotAction;
                if (isHot) Theme.FillRound(g, rects[i], Theme.S(6), Theme.Bg4);
                using (var f = Theme.IconFont(9.5f))
                    TextRenderer.DrawText(g, glyphs[i], f, rects[i], isHot ? hot[i] : normal[i],
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        /// <summary>
        /// 空列表时的引导（本程序不扫描系统，拖入是唯一的添加方式，所以提示必须清楚）。
        /// 画一个虚线框 + 三行说明，双击空白处可以直接打开添加对话框。
        /// </summary>
        private void DrawEmptyState(Graphics g)
        {
            int margin = Theme.S(40);
            int boxW = Math.Min(ClientSize.Width - margin * 2, Theme.S(560));
            if (boxW < Theme.S(200)) boxW = Math.Max(Theme.S(120), ClientSize.Width - margin);
            int boxH = Theme.S(168);
            var box = new Rectangle((ClientSize.Width - boxW) / 2,
                                    Math.Max(Theme.S(28), (ClientSize.Height - boxH) / 2 - Theme.S(20)),
                                    boxW, boxH);

            // 虚线框
            using (var p = new Pen(Theme.Line2, Math.Max(1f, 1.2f * Theme.Scale)))
            {
                p.DashStyle = DashStyle.Dash;
                p.DashPattern = new float[] { 4f, 3f };
                using (GraphicsPath path = Theme.RoundRect(box, Theme.S(12)))
                    g.DrawPath(p, path);
            }

            // 一个加号圆
            int iconSz = Theme.S(38);
            var iconBox = new Rectangle(box.X + (box.Width - iconSz) / 2, box.Y + Theme.S(20), iconSz, iconSz);
            using (var b = new SolidBrush(Color.FromArgb(0x3A, 0x46, 0x5E)))
                g.FillEllipse(b, iconBox);
            using (var f = Theme.IconFont(13f))
                TextRenderer.DrawText(g, "\uE710", f, iconBox, Theme.Ac2,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // 三行说明
            int y = iconBox.Bottom + Theme.S(12);
            using (var f = Theme.UiFont(13f, FontStyle.Bold))
                TextRenderer.DrawText(g, "把快捷方式拖到这里", f,
                    new Rectangle(box.X, y, box.Width, Theme.S(26)), Theme.Tx,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            y += Theme.S(26);

            using (var f = Theme.UiFont(9.5f))
            {
                TextRenderer.DrawText(g, "支持 .lnk 快捷方式 / .exe / .url / .bat / 文件夹 / 任意文档", f,
                    new Rectangle(box.X, y, box.Width, Theme.S(20)), Theme.Tx3,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                y += Theme.S(20);
                TextRenderer.DrawText(g, "也可以点上方「添加程序」，或双击这里", f,
                    new Rectangle(box.X, y, box.Width, Theme.S(20)), Theme.Tx3,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private int EnsureIcon(string target)
        {
            if (!Helper.HasText(target)) return -1;
            int idx;
            if (_iconIndex.TryGetValue(target, out idx)) return idx;
            if (_iconDrawnThisPass >= 12) { _iconPending = true; return -1; }
            _iconDrawnThisPass++;

            Icon ic = null;
            try { ic = AppIcon.Get(target); }
            catch { }
            if (ic == null) { _iconIndex[target] = -1; return -1; }

            try
            {
                using (Bitmap bmp = ic.ToBitmap())
                {
                    if (IconList == null) { _iconIndex[target] = -1; return -1; }
                    IconList.Images.Add(target, bmp);
                    idx = IconList.Images.Count - 1;
                    _iconIndex[target] = idx;
                    return idx;
                }
            }
            catch { _iconIndex[target] = -1; return -1; }
        }

        /// <summary>一轮重绘结束：重置配额，必要时补一次重绘把剩下的图标画完。</summary>
        public void AfterPaint()
        {
            _iconDrawnThisPass = 0;
            if (!_iconPending) return;
            _iconPending = false;
            if (_repaintBurst++ > 6) return;
            try { BeginInvoke(new MethodInvoker(delegate { Invalidate(); })); }
            catch { }
        }
    }

    // ------------------------------------------------------ 无边框窗口拖拽区

    internal sealed class DragBar : Panel
    {
        public DragBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private bool _dragging;
        private Point _start;
        private bool _maximized;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _start = e.Location;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging && e.Button == MouseButtons.Left)
            {
                Form f = FindForm();
                if (f != null)
                {
                    if (_maximized)
                    {
                        f.WindowState = FormWindowState.Normal;
                        _maximized = false;
                    }
                    f.Location = new Point(f.Location.X + (e.X - _start.X), f.Location.Y + (e.Y - _start.Y));
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            Form f = FindForm();
            if (f != null)
            {
                f.WindowState = (f.WindowState == FormWindowState.Maximized)
                    ? FormWindowState.Normal : FormWindowState.Maximized;
                _maximized = f.WindowState == FormWindowState.Maximized;
            }
            base.OnDoubleClick(e);
        }
    }
}
