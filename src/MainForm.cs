using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QuickLaunch
{
    internal sealed partial class MainForm : Form
    {
        // ------------------------------------------------------------ 字段

        private readonly AppConfig _cfg;
        private readonly long _configLoadMs;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, RowState> _states
            = new System.Collections.Concurrent.ConcurrentDictionary<long, RowState>();

        private volatile bool _stopRequested;
        private volatile bool _running;
        private long _runStarted;
        private int _runIndex, _runTotal;

        private string _selectedGroup = "all";
        private AppEntry _detail;
        private bool _dirty;
        private bool _suppressDetailEvents;
        private bool _configSaved;

        private ImageList _icons;
        private NotifyIcon _tray;

        private const string AllGroupKey = "all";
        private const string FavGroupKey = "fav";

        // ---- 控件
        private Panel _header, _side, _mid, _right, _logPanel, _footer;
        private FlowLayoutPanel _toolbar;   // 工具栏用流式布局：窗口窄了自动滚动，不挤按钮
        private Panel _rightCanvas;      // 右侧内容画布（固定高度，绝对坐标布局；滚动由自绘滚动条负责）
        internal int RightContentHeight; // 画布内容真实高度，供诊断用
        private DragBar _dragBar;
        private int _sideW = 238, _rightW = 292, _logH = 208, _headerH = 44, _footerH = 46;
        private GroupList _groupList;
        private AppListView _list;
        private LogView _logView;
        private TextBox _search;
        private ComboBox _filter;
        private QButton _btnAdd, _btnSave, _btnUpdate, _btnReload, _btnClear, _btnOpenLogs, _btnCheckAll;
        private QButton _btnRun, _btnStop, _btnMin, _btnMax, _btnClose, _btnTheme;
        private QButton _btnDelayMinus, _btnDelayPlus;
        private Label _lblDetailName, _lblDetailPath, _lblDetailGroup;
        private TextBox _txtDetailDelay;
        private QButton[] _segWindow;
        private QButton _swSerial, _swSkip, _swExplorer;
        private StatCard _cardSelected, _cardTime, _cardFound;
        private GroupBar _groupBar;
        private Label _lblFooterStat;
        private Label _lblFooterInfo;
        private Panel _footerSep;
        private Label _footerGroupCaption;
        private Label _lblSideHint;
        private System.Windows.Forms.Timer _uiTimer;

        private sealed class RowState
        {
            public int Status;        // 0 空闲 1 启动中 2 成功 3 失败 4 跳过
            public string Message = "";
        }

        // ------------------------------------------------------------ 构造

        public MainForm(AppConfig cfg, long configLoadMs)
        {
            _cfg = cfg;
            _configLoadMs = configLoadMs;

            // DPI 必须在建控件之前探测：所有写死的像素值都要按它缩放
            DpiInfo = Theme.InitDpi();

            // 按屏幕工作区适配窗口：150% 缩放下 1268x812 会变成 1902x1218，
            // 比 1080p 屏幕还高，直接用会伸到屏幕外面去
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int wantW = Theme.S(1268);
            int wantH = Theme.S(812);
            int w = Math.Min(wantW, wa.Width - Theme.S(12));
            int h = Math.Min(wantH, wa.Height - Theme.S(12));

            _sideW = Theme.S(238);
            _rightW = Theme.S(292);
            _headerH = Theme.S(44);
            _footerH = Theme.S(50);

            // 日志区高度按窗口实际高度取比例（21%，限幅 120~230 设计像素）。
            // 写死高度会出事：窗口矮的时候中间被挤没、右侧统计卡片整个看不见。
            int logH = (int)Math.Round(h * 0.21);
            if (logH < Theme.S(120)) logH = Theme.S(120);
            if (logH > Theme.S(230)) logH = Theme.S(230);
            _logH = logH;

            // 竖直方向挤不下时进一步收窄日志区，保证中间列表还能显示 5 行
            int chrome = _headerH + _footerH + Theme.S(54) + Theme.S(34) + _logH;
            if (h - chrome < Theme.S(340))
            {
                _logH = Theme.S(120);
            }

            // 宽度方向不再收缩左右两栏：右侧面板的内容是按固定设计宽度排版的，
            // 一旦收窄，缩放后的控件就会超出面板被裁掉（"最小化"变成"最小"）。
            // 窗口最小宽度由 MinimumSize 兜底，中间列表用 Dock=Fill 自然吃掉剩余空间。

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Text = "一键启动 · QuickLaunch";
            ClientSize = new Size(w, h);
            MinimumSize = new Size(Math.Min(Theme.S(980), wa.Width - Theme.S(40)), Theme.S(600));
            AutoScaleMode = AutoScaleMode.None;   // 自己按 DPI 缩放，交给 WinForms 会和手写布局打架
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Tx;
            Font = Theme.UiFont(11f);
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            DpiInfo = DpiInfo + string.Format("；窗口 {0}x{1}，屏幕工作区 {2}x{3}", w, h, wa.Width, wa.Height);

            BuildUi();
            WireEvents();
        }

        /// <summary>实际探测到的 DPI 信息，用于 --uicheck 输出。</summary>
        internal string DpiInfo = "";

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            OnFormLoad(this, e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 注意：被调用的处理函数刻意叫 HandleFormClosing 而不是 OnFormClosing，
            // 否则会和这个 override 同名 → 自己调自己 → 关窗口时 StackOverflow。
            // （这个 bug 真实发生过，被 tools\LayoutTest.cs 逮到）
            HandleFormClosing(e);
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            OnFormResize(this, e);
        }

        // ------------------------------------------------------------ 建界面

        private void BuildUi()
        {
            BuildHeader();

            _footer = new Panel();
            _footer.Dock = DockStyle.Bottom;
            _footer.Height = _footerH;
            _footer.BackColor = Theme.Bg2;
            _footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, 0, 0, _footer.Width, 0);
            };

            _logPanel = new Panel();
            _logPanel.Dock = DockStyle.Bottom;
            _logPanel.Height = _logH;
            _logPanel.BackColor = Theme.LogBg;

            _side = new Panel();
            _side.Dock = DockStyle.Left;
            _side.Width = _sideW;
            _side.BackColor = Theme.Bg2;

            _right = new Panel();
            _right.Dock = DockStyle.Right;
            _right.Width = _rightW;
            _right.BackColor = Theme.Bg2;
            // _right 本身不滚动：内容画布直接放进来，滚动位置由自绘滚动条控制

            _mid = new Panel();
            _mid.Dock = DockStyle.Fill;
            _mid.BackColor = Theme.Bg;

            BuildSide();
            BuildMid();
            BuildRight();
            BuildLogPanel();
            BuildFooter();

            // 注意添加顺序：Bottom 先加，Fill 最后加
            Controls.Add(_mid);
            Controls.Add(_side);
            Controls.Add(_right);
            Controls.Add(_logPanel);
            Controls.Add(_footer);
            Controls.Add(_header);

            BuildIconList();
            BuildTray();
        }

        private void BuildHeader()
        {
            _header = new Panel();
            _header.Dock = DockStyle.Top;
            _header.Height = _headerH;
            _header.BackColor = Theme.Bg2;

            _dragBar = new DragBar();
            _dragBar.Dock = DockStyle.Fill;
            _dragBar.BackColor = Theme.Bg2;
            _dragBar.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var logo = new Rectangle(Theme.S(14), Theme.S(12), Theme.S(20), Theme.S(20));
                using (LinearGradientBrush b = new LinearGradientBrush(logo,
                    Color.FromArgb(0x5B, 0x8B, 0xFF), Color.FromArgb(0x8F, 0x5B, 0xFF), 45f))
                    g.FillPath(b, Theme.RoundRect(logo, Theme.S(5)));

                using (var f = Theme.IconFont(10.5f))
                    TextRenderer.DrawText(g, "\uE945", f, logo, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                using (var tf = Theme.UiFont(11.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "一键启动", tf,
                        new Point(logo.Right + Theme.S(10), Theme.S(12)), Theme.Tx, TextFormatFlags.NoPrefix);

                using (var vf = Theme.UiFont(9.5f))
                    TextRenderer.DrawText(g, "QuickLaunch  v" + AppInfo.Version, vf,
                        new Point(logo.Right + Theme.S(86), Theme.S(15)), Theme.Tx3, TextFormatFlags.NoPrefix);

                using (var p = new Pen(Theme.Line))
                    g.DrawLine(p, 0, _header.Height - 1, _header.Width, _header.Height - 1);
            };
            _header.Controls.Add(_dragBar);

            // 窗口控制按钮：直接按坐标摆，不用 FlowLayoutPanel
            // （FlowLayoutPanel 在自绘控件 + 缩放字体下会把按钮挤出可见区域，这就是右上角按钮消失的原因）
            int bw = Theme.S(38), bh = Theme.S(30);
            int by = (_headerH - bh) / 2;

            _btnTheme = new QButton("", QButton.Look.Ghost);
            _btnTheme.Glyph = "\uE706";
            _btnTheme.Size = new Size(bw, bh);
            _btnTheme.Click += delegate { ToggleLightTheme(); };

            _btnMin = new QButton("", QButton.Look.Ghost);
            _btnMin.Glyph = "\uE921";
            _btnMin.Size = new Size(bw, bh);
            _btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };

            _btnMax = new QButton("", QButton.Look.Ghost);
            _btnMax.Glyph = "\uE922";
            _btnMax.Size = new Size(bw, bh);
            _btnMax.Click += delegate
            {
                WindowState = (WindowState == FormWindowState.Maximized)
                    ? FormWindowState.Normal : FormWindowState.Maximized;
            };

            _btnClose = new QButton("", QButton.Look.Ghost);
            _btnClose.Glyph = "\uE8BB";
            _btnClose.Size = new Size(Theme.S(42), bh);
            _btnClose.Click += delegate { Close(); };

            _dragBar.Controls.Add(_btnTheme);
            _dragBar.Controls.Add(_btnMin);
            _dragBar.Controls.Add(_btnMax);
            _dragBar.Controls.Add(_btnClose);

            LayoutHeaderButtons();
        }

        /// <summary>把右上角四个按钮贴到标题栏右侧。</summary>
        private void LayoutHeaderButtons()
        {
            if (_dragBar == null || _btnClose == null) return;
            int by = (_headerH - Theme.S(30)) / 2;
            int right = _dragBar.ClientSize.Width - Theme.S(8);

            _btnClose.Left = right - _btnClose.Width;
            right = _btnClose.Left;
            _btnMax.Left = right - _btnMax.Width;
            right = _btnMax.Left;
            _btnMin.Left = right - _btnMin.Width;
            right = _btnMin.Left;
            _btnTheme.Left = right - _btnTheme.Width;

            _btnTheme.Top = by;
            _btnMin.Top = by;
            _btnMax.Top = by;
            _btnClose.Top = by;

            _btnTheme.BringToFront();
            _btnMin.BringToFront();
            _btnMax.BringToFront();
            _btnClose.BringToFront();
        }

        private void BuildSide()
        {
            var title = new Label();
            title.Text = "  启动分组";
            title.Dock = DockStyle.Top;
            title.Height = Theme.S(34);
            title.ForeColor = Theme.Tx3;
            title.Font = Theme.UiFont(10.5f, FontStyle.Bold);
            title.TextAlign = ContentAlignment.MiddleLeft;

            var addGroup = new QButton("＋ 新建分组", QButton.Look.Ghost);
            addGroup.Dock = DockStyle.Bottom;
            addGroup.Height = Theme.S(30);
            addGroup.Margin = new Padding(Theme.S(8));
            addGroup.Click += OnClickAddGroup;

            _lblSideHint = new Label();
            _lblSideHint.Dock = DockStyle.Bottom;
            _lblSideHint.Height = Theme.S(40);
            _lblSideHint.ForeColor = Theme.Tx3;
            _lblSideHint.Font = Theme.UiFont(11f);
            _lblSideHint.Text = "  分组上点 ▶ 单独启动该组";
            _lblSideHint.TextAlign = ContentAlignment.MiddleLeft;

            _groupList = new GroupList();
            _groupList.Dock = DockStyle.Fill;

            var pad = new Panel();
            pad.Dock = DockStyle.Bottom;
            pad.Height = Theme.S(38);
            pad.Controls.Add(addGroup);

            _side.Controls.Add(_groupList);
            _side.Controls.Add(_lblSideHint);
            _side.Controls.Add(pad);
            _side.Controls.Add(title);
        }

        /// <summary>
        /// 造一个工具栏按钮：宽度按**实际渲染宽度**量出来（图标用图标字体、文字用界面字体分别量），
        /// 而不是写死设计值。写死的话字体一放大文字就超出按钮被截断。
        /// </summary>
        private QButton MkToolBtn(string text, string glyph, QButton.Look look)
        {
            var b = new QButton(text, look);
            b.Glyph = glyph;
            b.Size = new Size(b.MeasureContentWidth() + Theme.S(26), Theme.S(33));
            b.Margin = new Padding(0, 0, Theme.S(8), 0);
            return b;
        }

        private QButton MakeToggle(string text)
        {
            var b = new QButton(text, QButton.Look.Ghost);
            b.IsToggle = true;
            b.Height = Theme.S(26);
            b.Click += delegate { ToggleSwitch(b); };
            return b;
        }

        private void BuildMid()
        {
            // ============================================================
            // 工具栏：改成先「按实际文字量出宽度」再顺次排。
            // 之前写死 Size + 顺排，但宽度是按设计值给的，字体放大后
            // 文字就超出按钮被截断（截图里显示成「发现程」「更新路」…）。
            // ============================================================
            _search = new TextBox();
            _search.BorderStyle = BorderStyle.None;      // 去边框，外面包一层自己画的框，避免难看白边
            _search.BackColor = Theme.Bg3;
            _search.ForeColor = Theme.Tx;
            _search.Font = Theme.UiFont(11f);

            _filter = new ComboBox();
            _filter.DropDownStyle = ComboBoxStyle.DropDownList;
            _filter.FlatStyle = FlatStyle.Flat;
            _filter.BackColor = Theme.Bg3;
            _filter.ForeColor = Theme.Tx;
            _filter.Font = Theme.UiFont(11f);
            _filter.Items.AddRange(new object[] { "全部程序", "只看已勾选", "只看未勾选" });
            _filter.SelectedIndex = 0;
            // 自己画下拉项：默认渲染在深色主题下有白底和文字重影
            _filter.DrawMode = DrawMode.OwnerDrawFixed;
            _filter.ItemHeight = Theme.S(22);
            _filter.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using (var b = new SolidBrush(sel ? Theme.Bg4 : Theme.Bg3))
                    e.Graphics.FillRectangle(b, e.Bounds);
                if (e.Index >= 0 && e.Index < _filter.Items.Count)
                    TextRenderer.DrawText(e.Graphics, _filter.Items[e.Index].ToString(), _filter.Font,
                        new Rectangle(e.Bounds.X + Theme.S(6), e.Bounds.Y, e.Bounds.Width - Theme.S(10), e.Bounds.Height),
                        sel ? Color.White : Theme.Tx,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };

            _btnUpdate = MkToolBtn("更新路径", "\uE895", QButton.Look.Normal);
            // 本程序不扫描系统，所以「添加」是主操作，用主按钮样式突出出来
            _btnAdd = MkToolBtn("添加程序", "\uE710", QButton.Look.Primary);
            _btnSave = MkToolBtn("保存配置", "\uE74E", QButton.Look.Normal);
            _btnReload = MkToolBtn("重载", "\uE72C", QButton.Look.Ghost);
            _btnCheckAll = MkToolBtn("全选当前", "\uE73A", QButton.Look.Ghost);

            _toolbar = new FlowLayoutPanel();
            _toolbar.Dock = DockStyle.Top;
            _toolbar.Height = Theme.S(86);   // 够放两行按钮
            _toolbar.BackColor = Theme.Bg;
            _toolbar.FlowDirection = FlowDirection.LeftToRight;
            _toolbar.WrapContents = true;    // 窗口窄时按钮自动排到下一行，而不是被裁掉
            _toolbar.Padding = new Padding(Theme.S(14), Theme.S(10), 0, 0);
            _toolbar.AutoScroll = false;         // 换行即可，不需要滚动条

            var searchBox = new Panel();          // 给无边框搜索框套一个自己画的框
            searchBox.BackColor = Theme.Bg3;
            searchBox.Size = new Size(Theme.S(240), Theme.S(33));
            searchBox.Margin = new Padding(0, 0, Theme.S(8), 0);
            searchBox.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (var p = new Pen(Theme.Line2))
                    pe.Graphics.DrawRectangle(p, 0, 0, searchBox.Width - 1, searchBox.Height - 1);
            };
            _search.Location = new Point(Theme.S(8), Theme.S(7));
            _search.Width = searchBox.Width - Theme.S(16);
            searchBox.Controls.Add(_search);

            _filter.Margin = new Padding(0, Theme.S(1), Theme.S(8), 0);

            _toolbar.Controls.Add(searchBox);
            _toolbar.Controls.Add(_filter);
            _toolbar.Controls.Add(_btnUpdate);
            _toolbar.Controls.Add(_btnAdd);
            _toolbar.Controls.Add(_btnSave);
            _toolbar.Controls.Add(_btnReload);
            _toolbar.Controls.Add(_btnCheckAll);

            // 列标题（自绘列表没有系统表头，自己画一条）
            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = Theme.S(34);
            head.BackColor = Theme.Bg3;
            head.Paint += delegate(object s, PaintEventArgs pe)
            {
                Graphics g = pe.Graphics;
                var f = Theme.UiFont(9.5f);
                try
                {
                    int w = head.ClientSize.Width;
                    TextRenderer.DrawText(g, "程序", f, new Point(Theme.S(46), Theme.S(8)), Theme.Tx3, TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, "第二行显示来源与完整路径", f,
                        new Point(Theme.S(150), Theme.S(8)), Color.FromArgb(0x62, 0x6B, 0x7D), TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, "延迟", f, new Point(w - Theme.S(196), Theme.S(8)), Theme.Tx3, TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, "操作", f, new Point(w - Theme.S(106), Theme.S(8)), Theme.Tx3, TextFormatFlags.NoPrefix);
                }
                finally { f.Dispose(); }
                using (var p = new Pen(Theme.Line))
                    g.DrawLine(p, 0, head.Height - 1, head.Width, head.Height - 1);
            };

            // 程序列表：全自绘，行高完全自己说了算
            _list = new AppListView();
            _list.Dock = DockStyle.Fill;
            _list.BackColor = Theme.Bg;
            _list.ForeColor = Theme.Tx;
            _list.Font = Theme.UiFont(10.5f);
            _list.AllowDrop = true;
            _list.IconList = _icons;

            _mid.Controls.Add(_list);
            _mid.Controls.Add(head);
            _mid.Controls.Add(_toolbar);
        }


        private void ToggleSwitch(QButton b)
        {
            bool on = !b.TagIsOn;
            b.TagIsOn = on;
            b.Glyph = on ? "\uE73E" : "\uE739";
            b.Invalidate();
            SyncSwitchesToConfig();
        }

        // BuildRight 及右侧面板布局小工具已移到 MainForm.Right.cs

        private void BuildLogPanel()
        {
            var bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = Theme.S(34);
            bar.BackColor = Theme.Bg2;

            var dot = new Panel();
            dot.Size = new Size(Theme.S(8), Theme.S(8));
            dot.Location = new Point(Theme.S(14), Theme.S(13));
            dot.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Theme.Ok)) e.Graphics.FillEllipse(b, 0, 0, 8, 8);
            };

            var title = new Label();
            title.Text = "运行日志 / 调试信息";
            title.ForeColor = Theme.Tx2;
            title.Font = Theme.UiFont(11f, FontStyle.Bold);
            title.Location = new Point(Theme.S(30), Theme.S(8));
            title.Size = new Size(Theme.S(200), Theme.S(20));

            var chips = new FlowLayoutPanel();
            chips.Dock = DockStyle.Right;
            chips.Width = Theme.S(520);
            chips.FlowDirection = FlowDirection.RightToLeft;
            chips.WrapContents = false;
            chips.Padding = new Padding(Theme.S(0), Theme.S(5), Theme.S(10), Theme.S(0));
            chips.BackColor = Theme.Bg2;

            _btnOpenLogs = new QButton("打开日志文件夹", QButton.Look.Normal);
            _btnOpenLogs.Glyph = "\uE8B7";
            _btnOpenLogs.Size = new Size(_btnOpenLogs.MeasureContentWidth() + Theme.S(24), Theme.S(26));
            _btnOpenLogs.Margin = new Padding(Theme.S(4), Theme.S(0), Theme.S(0), Theme.S(0));
            _btnOpenLogs.Click += OnClickOpenLogs;

            _btnClear = new QButton("清空", QButton.Look.Ghost);
            Size needClear = TextRenderer.MeasureText(_btnClear.Text, _btnClear.Font);
            _btnClear.Size = new Size(needClear.Width + Theme.S(24), Theme.S(26));
            _btnClear.Margin = new Padding(Theme.S(4), Theme.S(0), Theme.S(0), Theme.S(0));
            _btnClear.Click += delegate { _logView.Clear(); };

            chips.Controls.Add(_btnOpenLogs);
            chips.Controls.Add(_btnClear);
            for (int i = 3; i >= 0; i--)
            {
                string[] names = new string[] { "全部", "信息", "警告", "错误" };
                LogLevel?[] lvls = new LogLevel?[] { null, LogLevel.INFO, LogLevel.WARN, LogLevel.ERR };
                var b = new QButton(names[i], QButton.Look.Ghost);
                b.IsToggle = true;
                Size needChip = TextRenderer.MeasureText(names[i], b.Font);
                b.Size = new Size(needChip.Width + Theme.S(24), Theme.S(26));
                b.Margin = new Padding(Theme.S(4), Theme.S(0), Theme.S(0), Theme.S(0));
                b.Tag = lvls[i];
                b.Click += OnClickLogFilter;
                chips.Controls.Add(b);
            }

            bar.Controls.Add(dot);
            bar.Controls.Add(title);
            bar.Controls.Add(chips);

            _logView = new LogView();
            _logView.Dock = DockStyle.Fill;

            _logPanel.Controls.Add(_logView);
            _logPanel.Controls.Add(bar);
        }

        private void BuildFooter()
        {
            _btnRun = new QButton("一键启动", QButton.Look.Primary);
            _btnRun.Glyph = "\uE768";
            _btnRun.Click += delegate { StartSequence(CollectChecked(), "一键启动"); };

            _btnStop = new QButton("停止", QButton.Look.Danger);
            _btnStop.Glyph = "\uE71A";
            _btnStop.Enabled = false;
            _btnStop.Click += delegate { _stopRequested = true; Logging.Warn("已收到停止请求，正在结束当前等待…"); };

            _lblFooterStat = new Label();
            _lblFooterStat.Text = "就绪";
            _lblFooterStat.ForeColor = Theme.Tx2;
            _lblFooterStat.Font = Theme.UiFont(10.5f);
            _lblFooterStat.TextAlign = ContentAlignment.MiddleLeft;
            _lblFooterStat.AutoSize = false;

            _footerSep = new Panel();
            _footerSep.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Line2))
                    e.Graphics.DrawLine(p, 0, 0, 0, _footerSep.Height);
            };

            _footerGroupCaption = new Label();
            _footerGroupCaption.Text = "分组启动";
            _footerGroupCaption.ForeColor = Theme.Tx3;
            _footerGroupCaption.Font = Theme.UiFont(10.5f);
            _footerGroupCaption.AutoSize = true;

            _groupBar = new GroupBar();

            _lblFooterInfo = new Label();
            _lblFooterInfo.Text = "列表 —";
            _lblFooterInfo.ForeColor = Theme.Tx3;
            _lblFooterInfo.Font = Theme.UiFont(9.5f);
            _lblFooterInfo.TextAlign = ContentAlignment.MiddleRight;
            _lblFooterInfo.AutoSize = false;

            // 底栏布局统一由 LayoutFooter() 负责（主按钮文字是动态的，宽度一变所有位置都要跟着挪）
            _footer.Controls.Add(_btnRun);
            _footer.Controls.Add(_btnStop);
            _footer.Controls.Add(_lblFooterStat);
            _footer.Controls.Add(_footerSep);
            _footer.Controls.Add(_footerGroupCaption);
            _footer.Controls.Add(_groupBar);
            _footer.Controls.Add(_lblFooterInfo);

            LayoutFooter();
        }

        /// <summary>
        /// 底栏整体重排。为什么必须是个方法：主按钮文字是动态的
        /// （「一键启动（15）」比「一键启动」宽得多），宽度一变后面所有控件的 Left 都要跟着挪。
        /// 不重排就会出现「一键启动（15）」压住「停止」—— tools\LayoutTest.cs 抓到的就是这个。
        /// </summary>
        private void LayoutFooter()
        {
            if (_btnRun == null || _footer == null) return;

            int pad = Theme.S(14);
            int gap = Theme.S(10);
            int fy = Theme.S(11);

            _btnRun.Size = new Size(_btnRun.MeasureContentWidth() + Theme.S(26), Theme.S(32));
            _btnRun.Location = new Point(pad, fy);

            _btnStop.Size = new Size(_btnStop.MeasureContentWidth() + Theme.S(26), Theme.S(32));
            _btnStop.Location = new Point(_btnRun.Right + Theme.S(8), fy);

            int x = _btnStop.Right + gap;
            _lblFooterStat.Location = new Point(x, fy);
            _lblFooterStat.Size = new Size(Theme.S(150), Theme.S(32));
            x = _lblFooterStat.Right + gap;

            if (_footerSep != null)
            {
                _footerSep.Size = new Size(1, Theme.S(18));
                _footerSep.Location = new Point(x, fy + Theme.S(7));
                x = _footerSep.Right + gap;
            }
            if (_footerGroupCaption != null)
            {
                _footerGroupCaption.Location = new Point(x, fy + Theme.S(7));
                x = _footerGroupCaption.Right + gap;
            }

            _lblFooterInfo.Size = new Size(Theme.S(230), Theme.S(32));
            _lblFooterInfo.Location = new Point(_footerW() - _lblFooterInfo.Width - pad, fy);

            _groupBar.Location = new Point(x, fy + Theme.S(4));
            _groupBar.Size = new Size(Math.Max(Theme.S(140), _lblFooterInfo.Left - x - Theme.S(12)), Theme.S(24));
            _groupBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        }

        private int _footerW() { return _footer == null ? 1268 : _footer.ClientSize.Width; }

        private void BuildIconList()
        {
            _icons = new ImageList();
            _icons.ImageSize = new Size(Theme.S(20), Theme.S(20));
            _icons.ColorDepth = ColorDepth.Depth32Bit;
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon != null ? Icon : SystemIcons.Application; }
            catch { _tray.Icon = SystemIcons.Application; }
            _tray.Text = "一键启动 QuickLaunch";
            _tray.Visible = false;

            var menu = new ContextMenuStrip();
            menu.Items.Add("显示主界面", null, delegate { ShowFromTray(); });
            menu.Items.Add("打开日志文件夹", null, OnClickOpenLogs);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { _configSaved = false; Close(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowFromTray(); };
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            _tray.Visible = false;
        }
    }
}
