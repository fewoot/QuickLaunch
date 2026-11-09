using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuickLaunch
{
    internal sealed partial class MainForm
    {
        /// <summary>
        /// 右侧设置面板。
        ///
        /// 踩过的坑（对照 参考资料\注意事项.md 第 8 / 9 / 11 条）：
        ///   1. 绝对坐标 + AutoScroll 会让 WinForms 平移子控件坐标，文字互相压住；
        ///   2. 内容总高超过面板高度时，底部控件被整个裁掉（统计卡片曾经看不见）；
        ///   3. 像素写死、字体随 DPI 放大 → 文字被截断。
        ///
        /// 现在的做法：
        ///   · AutoScroll 关掉，所有内容装进一个**固定高度**的内层画布 _rightCanvas；
        ///   · 画布高度由代码自顶向下累加算出（永远是内容真实高度），所以不可能重叠；
        ///   · 面板装不下时出现滚动条（外层 AutoScroll=false，改用 _rightScroll 承担）；
        ///   · 所有坐标都在设计像素下计算，运行时由 _rightCanvas 统一按 DPI 缩放。
        /// </summary>
        private void BuildRight()
        {
            const int M = 14;                 // 左右页边距（设计像素）
            int w = 288;                      // 画布设计宽度（100% 时）
            int inner = w - M * 2;
            int y = 10;

            _rightCanvas = new Panel();
            _rightCanvas.Name = "_rightCanvas";
            _rightCanvas.BackColor = Theme.Bg2;
            _rightCanvas.Location = new Point(0, 0);
            _rightCanvas.Width = w;           // 高度最后再定

            // ---------- 1. 选中程序
            y = Sec("选中程序", M, y, inner);

            _lblDetailName = MkLabel("（未选中）", Theme.Tx, 11.5f, FontStyle.Bold, M, y, inner, 24);
            y += 26;
            _lblDetailPath = MkMono("点一下列表里的程序，这里显示它的设置", M, y, inner, 34);
            y += 38;

            y = Cap("启动后等待（秒）", M, y, inner);

            _btnDelayMinus = MkBtn("−", QButton.Look.Normal, M, y, 36, 32);
            _btnDelayMinus.Click += delegate { ChangeDelay(-1); };

            _txtDetailDelay = new TextBox();
            _txtDetailDelay.Text = "3";
            _txtDetailDelay.TextAlign = HorizontalAlignment.Center;
            _txtDetailDelay.BorderStyle = BorderStyle.FixedSingle;
            _txtDetailDelay.BackColor = Theme.Bg3;
            _txtDetailDelay.ForeColor = Theme.Tx;
            _txtDetailDelay.Font = new Font("Consolas", 11f);
            _txtDetailDelay.Location = new Point(M + 44, y + 5);
            _txtDetailDelay.Width = 62;
            _txtDetailDelay.TextChanged += OnDetailDelayTextChanged;
            _txtDetailDelay.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
            };
            _rightCanvas.Controls.Add(_txtDetailDelay);

            _btnDelayPlus = MkBtn("＋", QButton.Look.Normal, M + 114, y, 36, 32);
            _btnDelayPlus.Click += delegate { ChangeDelay(1); };

            MkNote("启动下一个之前\n等待这么久", M + 158, y - 2, inner - 158, 34);
            y += 40;

            // 窗口状态：按可用宽度平分，不用固定间距（窄面板里会溢出）
            y = Cap("窗口状态", M, y, inner);
            _segWindow = new QButton[3];
            string[] winNames = new string[] { "正常", "最大化", "最小化" };
            int gap = 6;
            int segW = (inner - gap * 2) / 3;
            for (int i = 0; i < 3; i++)
            {
                var b = MkBtn(winNames[i], QButton.Look.Ghost, M + i * (segW + gap), y, segW, 30);
                b.Tag = i;
                b.Click += OnClickWindowState;
                _segWindow[i] = b;
            }
            y += 38;

            _lblDetailGroup = MkLabel("分组：—", Theme.Tx3, 10.5f, FontStyle.Regular, M, y, inner, 22);
            y += 28;

            y = Div(M, y, inner, w);

            // ---------- 2. 全局默认
            y = Sec("全局默认", M, y, inner);

            _swSerial = MkToggle("串行启动（一个一个来）", M, y, inner); y += 32;
            _swSkip = MkToggle("已在运行时跳过不重复开", M, y, inner); y += 32;
            _swExplorer = MkToggle("文件夹用资源管理器打开", M, y, inner); y += 32;

            MkNote("需要管理员的程序，启动时会自己弹 UAC，你在弹窗里点「是」即可。", M, y, inner, 34);
            y += 42;

            y = Div(M, y, inner, w);

            // ---------- 3. 本次启动
            y = Sec("本次启动", M, y, inner);

            int cardW = (inner - 8) / 2;
            _cardSelected = MkCard("已勾选", M, y, cardW, 56);
            _cardTime = MkCard("预计耗时", M + cardW + 8, y, cardW, 56);
            y += 64;
            _cardFound = MkCard("列表里的程序", M, y, inner, 56);
            y += 64;

            // ---------- DPI 归一化（关键）
            // 上面所有控件都是按「设计像素」(100% 时的像素) 摆的，
            // 而字体是磅值、会随 DPI 自动放大。如果不同步缩放控件矩形，
            // 150% 下就会出现「字变大了但框没变大」——文字溢出、控件挤在左上角。
            // 这里统一乘一次缩放系数即可，字体保持不变（它已经是 DPI 正确的）。
            foreach (Control c in _rightCanvas.Controls)
            {
                c.Bounds = new Rectangle(Theme.S(c.Left), Theme.S(c.Top),
                                         Theme.S(c.Width), Theme.S(c.Height));
            }

            // 画布高度 = 内容真实总高（已缩放）
            int contentH = Theme.S(y + 4);
            // 给自绘滚动条留出右边一条，避免内容被压在滚动条底下
            _rightCanvas.Width = _rightW - RightSbW - Theme.S(4);
            _rightCanvas.Height = contentH;
            _rightCanvas.Left = 0;
            _rightCanvas.Top = 0;

            // 不再用 Panel.AutoScroll —— 它画出来的是白色原生滚动条，跟深色界面完全不搭。
            // 改成自己管滚动位置 + 自绘一条细滑块（和左侧程序列表一致）。
            _right.Controls.Add(_rightCanvas);

            _right.Paint += OnRightPaint;
            _right.MouseWheel += OnRightWheel;
            _right.MouseDown += OnRightMouseDown;
            _right.MouseMove += OnRightMouseMove;
            _right.MouseUp += OnRightMouseUp;
            _right.MouseLeave += delegate { if (_rightSbHot) { _rightSbHot = false; _right.Invalidate(); } };
            _right.Resize += delegate { ClampRightScroll(); _right.Invalidate(); };
            _rightCanvas.MouseWheel += OnRightWheel;

            // 记录内容高度，供诊断使用
            RightContentHeight = contentH;
            ApplyRightScroll();
        }

        // ==================================================== 右侧面板自绘滚动条

        private int _rightScrollTop;
        private bool _rightSbHot;
        private bool _rightSbDrag;
        private int _rightDragStartY;
        private int _rightDragStartTop;

        private int RightSbW { get { return Theme.S(8); } }

        /// <summary>可滚动的最大距离（内容高度 - 可视高度）。</summary>
        internal int RightMaxScroll
        {
            get { return Math.Max(0, _rightCanvas.Height - _right.ClientSize.Height); }
        }

        private Rectangle RightTrack()
        {
            return new Rectangle(_right.ClientSize.Width - RightSbW - Theme.S(2), Theme.S(6),
                                 RightSbW, Math.Max(1, _right.ClientSize.Height - Theme.S(12)));
        }

        private Rectangle RightThumb()
        {
            int max = RightMaxScroll;
            if (max <= 0) return Rectangle.Empty;

            Rectangle track = RightTrack();
            int contentH = Math.Max(1, _rightCanvas.Height);
            int thumbH = (int)Math.Max(Theme.S(30), (long)track.Height * _right.ClientSize.Height / contentH);
            if (thumbH > track.Height) thumbH = track.Height;

            int range = track.Height - thumbH;
            int pos = range <= 0 ? 0 : (int)((long)range * _rightScrollTop / max);
            return new Rectangle(track.X, track.Y + pos, RightSbW, thumbH);
        }

        private void OnRightPaint(object sender, PaintEventArgs e)
        {
            if (RightMaxScroll <= 0) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            Theme.FillRound(g, RightTrack(), RightSbW / 2, Color.FromArgb(0x28, 0x2E, 0x3A));
            Theme.FillRound(g, RightThumb(), RightSbW / 2,
                (_rightSbDrag || _rightSbHot) ? Color.FromArgb(0x6B, 0x77, 0x8C) : Color.FromArgb(0x4A, 0x54, 0x68));
        }

        private void OnRightWheel(object sender, MouseEventArgs e)
        {
            int max = RightMaxScroll;
            if (max <= 0) return;

            _rightScrollTop -= e.Delta / 120 * Theme.S(48);
            ClampRightScroll();
            ApplyRightScroll();
            if (sender is Control) ((Control)sender).Invalidate();
            _right.Invalidate();
        }

        private void ClampRightScroll()
        {
            int max = RightMaxScroll;
            if (_rightScrollTop < 0) _rightScrollTop = 0;
            if (_rightScrollTop > max) _rightScrollTop = max;
        }

        private void ApplyRightScroll()
        {
            ClampRightScroll();
            if (_rightCanvas.Top != -_rightScrollTop) _rightCanvas.Top = -_rightScrollTop;
        }

        private void OnRightMouseDown(object sender, MouseEventArgs e)
        {
            if (RightMaxScroll <= 0) return;

            Rectangle thumb = RightThumb();
            if (thumb.Contains(e.Location))
            {
                _rightSbDrag = true;
                _rightDragStartY = e.Y;
                _rightDragStartTop = _rightScrollTop;
                _right.Capture = true;
                _right.Invalidate();
            }
            else if (RightTrack().Contains(e.Location))
            {
                _rightScrollTop += e.Y < thumb.Y ? -_right.ClientSize.Height / 2 : _right.ClientSize.Height / 2;
                ApplyRightScroll();
                _right.Invalidate();
            }
        }

        private void OnRightMouseMove(object sender, MouseEventArgs e)
        {
            if (RightMaxScroll <= 0) return;

            if (_rightSbDrag)
            {
                Rectangle track = RightTrack();
                int thumbH = RightThumb().Height;
                int range = Math.Max(1, track.Height - thumbH);
                int max = RightMaxScroll;
                int pos = _rightDragStartTop + (e.Y - _rightDragStartY);
                // 按比例把像素位移换算成滚动距离
                int delta = (int)((long)(pos - _rightDragStartTop) * max / Math.Max(1, range));
                _rightScrollTop = _rightDragStartTop + delta;
                ApplyRightScroll();
                _right.Invalidate();
                return;
            }

            bool hot = RightThumb().Contains(e.Location);
            if (hot != _rightSbHot)
            {
                _rightSbHot = hot;
                _right.Cursor = hot ? Cursors.Hand : Cursors.Default;
                _right.Invalidate();
            }
        }

        private void OnRightMouseUp(object sender, MouseEventArgs e)
        {
            if (!_rightSbDrag) return;
            _rightSbDrag = false;
            _right.Capture = false;
            _right.Invalidate();
        }

        // ==================================================== 布局小工具
        // 统一在这里 Add 到画布，避免「忘了加进 Controls」这种低级错误

        private int Sec(string text, int x, int y, int w)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = Theme.Tx3;
            l.Font = Theme.UiFont(9f, FontStyle.Bold);
            l.Location = new Point(x, y);
            l.Size = new Size(w, 22);
            l.TextAlign = ContentAlignment.MiddleLeft;
            _rightCanvas.Controls.Add(l);
            return y + 26;
        }

        private int Cap(string text, int x, int y, int w)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = Theme.Tx2;
            l.Font = Theme.UiFont(10.5f);
            l.Location = new Point(x, y);
            l.Size = new Size(w, 20);
            _rightCanvas.Controls.Add(l);
            return y + 22;
        }

        private void MkNote(string text, int x, int y, int w, int h)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = Theme.Tx3;
            l.Font = Theme.UiFont(9.5f);
            l.Location = new Point(x, y);
            l.Size = new Size(Math.Max(40, w), h);
            _rightCanvas.Controls.Add(l);
        }

        private Label MkLabel(string text, Color color, float size, FontStyle style, int x, int y, int w, int h)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.Font = Theme.UiFont(size, style);
            l.AutoEllipsis = true;
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            _rightCanvas.Controls.Add(l);
            return l;
        }

        private Label MkMono(string text, int x, int y, int w, int h)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = Theme.Tx3;
            l.Font = new Font("Consolas", 9f);
            l.AutoEllipsis = true;
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            _rightCanvas.Controls.Add(l);
            return l;
        }

        private QButton MkBtn(string text, QButton.Look look, int x, int y, int w, int h)
        {
            var b = new QButton(text, look);
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            _rightCanvas.Controls.Add(b);
            return b;
        }

        private QButton MkToggle(string text, int x, int y, int w)
        {
            var b = new QButton(text, QButton.Look.Ghost);
            b.IsToggle = true;
            b.Location = new Point(x, y);
            b.Size = new Size(w, 26);
            b.Click += delegate { ToggleSwitch(b); };
            _rightCanvas.Controls.Add(b);
            return b;
        }

        private StatCard MkCard(string caption, int x, int y, int w, int h)
        {
            var c = new StatCard();
            c.Caption = caption;
            c.Location = new Point(x, y);
            c.Size = new Size(w, h);
            _rightCanvas.Controls.Add(c);
            return c;
        }

        private int Div(int x, int y, int w, int canvasW)
        {
            var p = new Panel();
            p.Location = new Point(x, y);
            p.Size = new Size(w, 1);
            p.BackColor = Theme.Line;
            _rightCanvas.Controls.Add(p);
            return y + 14;
        }
    }
}
