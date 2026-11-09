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
    internal sealed partial class MainForm
    {
        // =========================================================== 事件挂接

        private void WireEvents()
        {
            _list.CheckToggled += delegate(object s, AppListView.Item it) { ToggleEntry(it.Entry); };
            _list.ActionLaunch += delegate(object s, AppListView.Item it) { LaunchSingle(it.Entry); };
            _list.ActionFolder += delegate(object s, AppListView.Item it) { OpenTargetFolder(it.Entry); };
            _list.ActionRemove += delegate(object s, AppListView.Item it) { RemoveEntry(it.Entry); };
            _list.DelayEditRequested += delegate(object s, AppListView.Item it) { EditDelay(it.Entry); };
            // 列表为空时双击空白处 = 打开"添加程序"对话框（拖入是主路径，这是备用入口）
            _list.EmptyAreaActivated += delegate { AddFromDialog(); };
            _list.ItemSelected += delegate(object s, AppListView.Item it)
            {
                _detail = it.Entry;
                UpdateDetailFromSelection();
            };
            _list.Paint += delegate { _list.AfterPaint(); };

            _search.TextChanged += delegate { RefreshList(); };
            _filter.SelectedIndexChanged += delegate { RefreshList(); };


            _btnUpdate.Click += OnClickUpdatePaths;
            _btnAdd.Click += delegate { AddFromDialog(); };
            _btnSave.Click += delegate { SaveConfig(true); };
            _btnReload.Click += OnClickReload;
            _btnCheckAll.Click += OnClickCheckAllVisible;
            _btnClose.Click += delegate { Close(); };

            _btnDelayMinus.Click += delegate { ChangeDelay(-1); };
            _btnDelayPlus.Click += delegate { ChangeDelay(1); };
            _txtDetailDelay.TextChanged += OnDetailDelayTextChanged;
            _txtDetailDelay.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
            };

            _groupList.SelectionChanged += delegate { OnGroupSelected(); };
            _groupList.PlayClicked += delegate(object s, GroupList.Row r) { OnGroupPlay(r); };
            _groupBar.ItemClicked += delegate(object s, GroupBar.Item it) { OnGroupBarClick(it); };

            _list.AllowDrop = true;
            _list.DragEnter += OnDragEnter;
            _list.DragDrop += OnDragDrop;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            KeyDown += OnKeyDown;

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 120;
            _uiTimer.Tick += OnUiTick;

            Resize += OnFormResize;
        }

        // =========================================================== 生命周期

        private void OnFormLoad(object sender, EventArgs e)
        {
            Logging.Info(string.Format("界面已就绪，配置载入耗时 {0} 毫秒。", _configLoadMs));
            Logging.Info("DPI：" + DpiInfo);

            SyncConfigToSwitches();
            RebuildGroups();
            RefreshList();
            UpdateRunStats();
            LayoutHeaderButtons();
            _uiTimer.Start();

            Logging.Info("日志文件：" + Logging.FilePath);
            Logging.Info("启动方式：CreateProcess（原生进程创建，不经过 cmd）；提权/文件夹/网址走 ShellExecuteEx。");
            Logging.Info("用法：把快捷方式 / exe / 文件夹拖进列表 → 打勾 → 点左下角「一键启动」。");
            ShowStartupHints();
        }

        /// <summary>
        /// 启动后的引导。本程序**不扫描**系统已装程序，列表内容全部来自你自己的添加，
        /// 所以空的时侯必须把「怎么添加」说清楚。
        /// </summary>
        private void ShowStartupHints()
        {
            if (_cfg.Entries.Count == 0)
            {
                Logging.Warn("列表是空的 —— 本程序不扫描系统，需要你手动添加。");
                Logging.Info("把桌面/开始菜单里的快捷方式直接拖到下面窗口就行；或者点「添加程序」手动选。");
                Logging.Info("支持拖入：.lnk 快捷方式、.exe 程序、.url 网址、.bat/.cmd、文件夹、任意文档。");
                Logging.Info("如果拖不进来：检查本程序是不是被以管理员身份运行了（那样 Windows 禁止拖放）。");
            }
            else
            {
                Logging.Info(string.Format("已载入你自己的 {0} 个程序（不扫描系统，全部手动添加）。", _cfg.Entries.Count));
            }
        }

        /// <summary>
        /// 关闭窗口时的清理。名字**不能**叫 OnFormClosing —— 那会和外层的 override 同名，
        /// 变成自己调自己，关窗口直接 StackOverflow（真实踩过）。
        /// </summary>
        private void HandleFormClosing(FormClosingEventArgs e)
        {
            if (_running)
            {
                DialogResult r = MessageBox.Show("正在启动程序，确定要退出吗？\n未启动的项目将不会被执行。",
                    "一键启动", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                _stopRequested = true;
            }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }

            if (_dirty || !_configSaved) SaveConfig(false);
            if (_uiTimer != null) _uiTimer.Stop();
            Logging.Info("界面已关闭。");
        }

        // =========================================================== 主循环

        private void OnUiTick(object sender, EventArgs e)
        {
            List<Logging.LogItem> batch = Logging.TakeNew();
            if (batch.Count > 0) _logView.AddRange(batch);
        }

        private void OnFormResize(object sender, EventArgs e)
        {
            LayoutHeaderButtons();
            if (_footer == null) return;
            try
            {
                _lblFooterInfo.Left = _footer.ClientSize.Width - _lblFooterInfo.Width - Theme.S(14);
                int w = _lblFooterInfo.Left - _groupBar.Left - Theme.S(14);
                if (w > Theme.S(120)) _groupBar.Width = w;
            }
            catch { }
        }

        // =========================================================== 分组

        private void RebuildGroups()
        {
            var rows = new List<GroupList.Row>();

            var all = new GroupList.Row();
            all.Key = AllGroupKey;
            all.Icon = "\uE8FD";
            all.Text = "全部程序";
            all.Count = _cfg.Entries.Count;
            all.ShowPlay = true;
            all.Selected = _selectedGroup == AllGroupKey;
            rows.Add(all);

            foreach (LaunchGroup g in _cfg.Groups)
            {
                var r = new GroupList.Row();
                r.Key = g.Key;
                r.Icon = Helper.HasText(g.Icon) ? g.Icon : "\uE8FD";
                r.Text = g.Name;
                r.Count = CountInGroup(g.Key);
                r.ShowPlay = true;
                r.Selected = _selectedGroup == g.Key;
                rows.Add(r);
            }

            var fav = new GroupList.Row();
            fav.Key = FavGroupKey;
            fav.Icon = "\uE734";
            fav.Text = "常用收藏";
            fav.Count = CountFavorites();
            fav.ShowPlay = true;
            fav.Selected = _selectedGroup == FavGroupKey;
            rows.Add(fav);

            _groupList.SetRows(rows);
            RebuildGroupBarCounts();
        }

        private int CountInGroup(string key)
        {
            int n = 0;
            foreach (AppEntry e in _cfg.Entries) if (e.Group == key) n++;
            return n;
        }

        private int CountFavorites()
        {
            int n = 0;
            foreach (AppEntry e in _cfg.Entries) if (e.Favorite) n++;
            return n;
        }

        private int CountCheckedInGroup(string key)
        {
            int n = 0;
            foreach (AppEntry e in _cfg.Entries)
                if (e.Enabled && e.Group == key) n++;
            return n;
        }

        private void OnGroupSelected()
        {
            _selectedGroup = _groupList.SelectedKey;
            RefreshList();
        }

        private void OnGroupPlay(GroupList.Row r)
        {
            if (r == null) return;
            List<AppEntry> items = CollectCheckedInGroup(r.Key);
            if (items.Count == 0)
            {
                Logging.Warn("「" + r.Text + "」里还没有勾选任何程序，先在列表里打勾。");
                return;
            }
            StartSequence(items, r.Text);
        }

        private void OnGroupBarClick(GroupBar.Item it)
        {
            if (it == null) return;
            List<AppEntry> items = CollectCheckedInGroup(it.Key);
            if (items.Count == 0)
            {
                Logging.Warn("「" + it.Text + "」里还没有勾选任何程序。");
                return;
            }
            StartSequence(items, it.Text);
        }

        private List<AppEntry> CollectCheckedInGroup(string key)
        {
            var list = new List<AppEntry>();
            foreach (AppEntry e in _cfg.Entries)
            {
                if (!e.Enabled) continue;
                if (key == AllGroupKey) { list.Add(e); continue; }
                if (key == FavGroupKey) { if (e.Favorite) list.Add(e); continue; }
                if (e.Group == key) list.Add(e);
            }
            return list;
        }

        private List<AppEntry> CollectChecked()
        {
            var list = new List<AppEntry>();
            foreach (AppEntry e in _cfg.Entries) if (e.Enabled) list.Add(e);
            return list;
        }

        private void OnClickAddGroup(object sender, EventArgs e)
        {
            string name = Prompt("新建分组", "给分组起个名字，例如：早上开工 / 摸鱼必备", "新分组");
            if (!Helper.HasText(name)) return;

            string key = "g" + DateTime.Now.ToString("HHmmssfff");
            _cfg.Groups.Add(new LaunchGroup(key, name.Trim(), "\uE8FD"));
            _selectedGroup = key;
            MarkDirty();
            RebuildGroups();
            RefreshList();
            Logging.Ok("已新建分组：「" + name.Trim() + "」。");
        }

        // =========================================================== 列表刷新

        private void RefreshList()
        {
            string q = _search.Text == null ? "" : _search.Text.Trim().ToLowerInvariant();
            int filterMode = _filter.SelectedIndex;

            var items = new List<AppListView.Item>();
            foreach (AppEntry e in _cfg.Entries)
            {
                if (_selectedGroup == FavGroupKey) { if (!e.Favorite) continue; }
                else if (_selectedGroup != AllGroupKey) { if (e.Group != _selectedGroup) continue; }

                if (filterMode == 1 && !e.Enabled) continue;
                if (filterMode == 2 && e.Enabled) continue;

                if (q.Length > 0)
                {
                    string hay = (e.Name + " " + e.Target + " " + e.Source).ToLowerInvariant();
                    if (hay.IndexOf(q, StringComparison.Ordinal) < 0) continue;
                }

                var it = new AppListView.Item();
                it.Entry = e;
                DescribeSource(e, it);

                RowState st = StateOf(e);
                if (st != null) { it.Status = st.Status; it.Message = st.Message; }

                items.Add(it);
            }

            _list.SetItems(items);
            UpdateFooterInfo();
            UpdateRunStats();
            _groupList.Invalidate();
            _groupBar.Invalidate();
        }

        private void DescribeSource(AppEntry e, AppListView.Item it)
        {
            if (!Helper.HasText(e.Target))
            {
                it.SourceText = "目标为空";
                it.SourceColor = Theme.Err;
                return;
            }

            bool shell = Helper.LooksLikeShellTarget(e.Target);
            bool url = Helper.LooksLikeUrl(e.Target);
            bool folder = false;
            bool exists = true;

            if (!shell && !url)
            {
                try
                {
                    folder = Directory.Exists(e.Target);
                    exists = folder || File.Exists(e.Target);
                }
                catch { exists = false; }
            }

            if (!exists)
            {
                it.SourceText = "⚠ 目标不存在（可能已卸载）";
                it.SourceColor = Theme.Err;
                return;
            }

            string kind = e.Source;
            if (shell) kind = "现代应用";
            else if (url) kind = "网址";
            else if (folder) kind = "文件夹";

            if (Helper.HasText(e.Note) && e.Note.IndexOf("管理员", StringComparison.Ordinal) >= 0)
            {
                it.SourceText = kind + " · 可能需管理员";
                it.SourceColor = Theme.Warn;
            }
            else
            {
                it.SourceText = kind;
                it.SourceColor = e.Source == "注册表" ? Theme.Ok
                    : e.Source == "开始菜单" ? Theme.Warn
                    : e.Source == "现代应用" ? Theme.Ac2
                    : Theme.Tx3;
            }
        }

        private void UpdateFooterInfo()
        {
            int checkedCount = 0;
            foreach (AppEntry e in _cfg.Entries) if (e.Enabled) checkedCount++;
            _lblFooterInfo.Text = string.Format("列表 {0} 项 · 已勾选 {1} · 全部手动添加",
                _cfg.Entries.Count, checkedCount);
        }

        private void UpdateRunStats()
        {
            int n = 0, sec = 0;
            foreach (AppEntry e in _cfg.Entries)
            {
                if (!e.Enabled) continue;
                n++;
                sec += e.DelaySeconds;
            }

            _cardSelected.Value = n + " 个";
            _cardTime.Value = Helper.HumanSeconds(sec);
            _cardTime.ValueColor = sec > 90 ? Theme.Warn : Theme.Tx;
            _cardFound.Value = _cfg.Entries.Count + " 个";
            _cardSelected.Invalidate();
            _cardTime.Invalidate();
            _cardFound.Invalidate();

            // 文字是动态的（「一键启动（12）」比「一键启动」宽），所以宽度也要跟着重算，
            // 否则数字一变就被截断（截图里出现过「一键启动（4…」）
            _btnRun.Text = "一键启动（" + n + "）";
            LayoutFooter();          // 文字宽度变了，底栏所有控件都要重新排
            _btnRun.Invalidate();
            _btnRun.Enabled = !_running && n > 0;
            _btnStop.Enabled = _running;

            RebuildGroupBarCounts();
        }

        private void RebuildGroupBarCounts()
        {
            var items = new List<GroupBar.Item>();
            foreach (LaunchGroup g in _cfg.Groups)
            {
                var it = new GroupBar.Item();
                it.Key = g.Key;
                it.Text = g.Name;
                it.Count = CountCheckedInGroup(g.Key);
                items.Add(it);
            }
            _groupBar.SetItems(items);
        }

        private void UpdateDetailFromSelection()
        {
            if (_detail == null)
            {
                _lblDetailName.Text = "（未选中）";
                _lblDetailPath.Text = "点一下列表里的程序，这里显示它的详细设置";
                _lblDetailGroup.Text = "分组：—";
                return;
            }

            _suppressDetailEvents = true;
            _lblDetailName.Text = _detail.Name;
            _lblDetailPath.Text = _detail.Target;
            _lblDetailGroup.Text = "分组：" + _cfg.GroupName(_detail.Group)
                + (_detail.Enabled ? "      ● 已勾选" : "      ○ 未勾选");
            _txtDetailDelay.Text = _detail.DelaySeconds.ToString();
            _suppressDetailEvents = false;

            for (int i = 0; i < _segWindow.Length; i++)
            {
                _segWindow[i].Style = i == _detail.WindowState ? QButton.Look.Primary : QButton.Look.Ghost;
                _segWindow[i].Invalidate();
            }
        }

        private void ChangeDelay(int delta)
        {
            if (_detail == null)
            {
                Logging.Warn("先在列表里选中一个程序，才能改它的延迟。");
                return;
            }
            _detail.DelaySeconds = _detail.DelaySeconds + delta;
            _dirty = true;
            UpdateDetailFromSelection();
            _list.Invalidate();
            UpdateRunStats();
        }

        /// <summary>双击延迟徽标时弹框改延迟。</summary>
        private void EditDelay(AppEntry entry)
        {
            string s = Prompt("修改启动延迟",
                "启动「" + entry.Name + "」之后，等多少秒再启动下一个？（0 = 不等待）",
                entry.DelaySeconds.ToString());
            int v;
            if (int.TryParse(s, out v) && v >= 0 && v <= 3600)
            {
                entry.DelaySeconds = v;
                _dirty = true;
                Logging.Info(string.Format("「{0}」的延迟改为 {1} 秒。", entry.Name, v));
                _detail = entry;
                RefreshList();
                UpdateDetailFromSelection();
            }
        }

        private void OnDetailDelayTextChanged(object sender, EventArgs e)
        {
            if (_suppressDetailEvents || _detail == null) return;
            int v;
            if (int.TryParse(_txtDetailDelay.Text, out v) && v >= 0 && v <= 3600)
            {
                _detail.DelaySeconds = v;
                _dirty = true;
                _list.Invalidate();
                UpdateRunStats();
            }
        }

        private void OnClickWindowState(object sender, EventArgs e)
        {
            if (_detail == null) return;
            var b = sender as QButton;
            if (b == null) return;
            _detail.WindowState = (int)b.Tag;
            _dirty = true;
            UpdateDetailFromSelection();
        }

        private RowState StateOf(AppEntry e)
        {
            RowState st;
            if (_states.TryGetValue(e.Id, out st)) return st;
            return null;
        }

        // =========================================================== 列表操作

        private void ToggleEntry(AppEntry entry)
        {
            entry.Enabled = !entry.Enabled;
            _dirty = true;
            if (entry.Enabled && entry.IsNew)
            {
                entry.IsNew = false;
                Logging.Ok("已加入启动列表：「" + entry.Name + "」");
            }
            else
            {
                Logging.Info((entry.Enabled ? "勾选 " : "取消勾选 ") + entry.Name);
            }
            _detail = entry;
            RefreshList();
            UpdateDetailFromSelection();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                RefreshList();
                Logging.Info("已刷新列表（F5）。");
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.S) { SaveConfig(true); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.F)
            {
                _search.Focus();
                _search.SelectAll();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Escape && _running) { _stopRequested = true; e.Handled = true; }
        }

        private void LaunchSingle(AppEntry entry)
        {
            Logging.Info("单独启动：" + entry.Name);
            var t = new Thread(delegate()
            {
                LaunchResult r;
                try { r = Launcher.Launch(entry, _cfg.FoldersInExplorer); }
                catch (Exception ex) { Logging.Exception("单独启动「" + entry.Name + "」", ex); return; }

                if (r.Ok)
                    Logging.Ok(string.Format("  ✔ {0}    PID {1}    用时 {2} ms", entry.Name, r.ProcessId, r.ElapsedMs));
                else
                    Logging.Err("  ✖ " + entry.Name + " 启动失败：" + r.Message);
            });
            t.IsBackground = true;
            t.Start();
        }

        private void OpenTargetFolder(AppEntry entry)
        {
            string dir = Helper.TargetFolder(entry.Target);
            if (!Helper.HasText(dir))
            {
                Logging.Warn("「" + entry.Name + "」不是本地文件，没有可以打开的文件夹。");
                return;
            }

            // 交给 shell 打开，不自己 spawn explorer.exe（那样会引发 0xc0000142 启动失败）
            LaunchResult r = Launcher.OpenInExplorer(dir);
            if (r.Ok) Logging.Info("已打开文件夹：" + dir);
            else Logging.Err("打开文件夹失败：" + r.Message);
        }

        private void RemoveEntry(AppEntry entry)
        {
            DialogResult r = MessageBox.Show(
                "把「" + entry.Name + "」从启动列表里移除？\n（只是不再显示在这里，不会卸载这个软件）",
                "移除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            _cfg.Entries.Remove(entry);
            RowState removed;
            _states.TryRemove(entry.Id, out removed);
            if (_detail == entry) _detail = null;
            MarkDirty();
            Logging.Warn("已移除：「" + entry.Name + "」（退出时会自动保存）");
            RebuildGroups();
            RefreshList();
            UpdateDetailFromSelection();
        }

        // =========================================================== 添加程序

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                _list.BackColor = Color.FromArgb(0x2A, 0x39, 0x55);
                _list.Invalidate();
            }
            else e.Effect = DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            _list.BackColor = Theme.Bg;
            _list.Invalidate();

            string[] files = null;
            try { files = e.Data.GetData(DataFormats.FileDrop) as string[]; }
            catch { }

            if (files == null || files.Length == 0)
            {
                Logging.Warn("没有接收到有效文件。提示：本程序以管理员身份运行时，Windows 会禁止拖放。");
                return;
            }

            Logging.Info(string.Format("接收到拖入的 {0} 个对象，开始解析…", files.Length));
            int added = 0;
            foreach (string f in files)
            {
                try { if (AddOnePath(f, true)) added++; }
                catch (Exception ex) { Logging.Exception("解析拖入对象「" + Helper.ExeName(f) + "」", ex); }
            }

            if (added > 0)
            {
                MarkDirty();
                RebuildGroups();
                RefreshList();
                Logging.Ok(string.Format("已添加 {0} 项（退出时会自动保存配置）。", added));
            }
            else Logging.Warn("这次拖入没有添加任何项目。");
        }

        private void AddFromDialog()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要加入启动列表的程序、快捷方式或文档";
                dlg.Filter = "所有可启动对象|*.exe;*.lnk;*.url;*.bat;*.cmd;*.ps1|程序 (*.exe)|*.exe|快捷方式 (*.lnk)|*.lnk|所有文件 (*.*)|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                int added = 0;
                foreach (string f in dlg.FileNames) if (AddOnePath(f, true)) added++;
                if (added > 0)
                {
                    MarkDirty();
                    RebuildGroups();
                    RefreshList();
                    Logging.Ok(string.Format("已添加 {0} 项。", added));
                }
            }
        }

        private bool AddOnePath(string path, bool enable)
        {
            if (!Helper.HasText(path)) return false;

            string target = path, args = "", cwd = "", source = "手动添加", name = "";
            string ext = "";
            try { ext = Path.GetExtension(path).ToLowerInvariant(); }
            catch { }

            if (ext == ".lnk" || ext == ".url")
            {
                ShortcutResolver.Target t = ShortcutResolver.Resolve(path);
                if (t == null || !Helper.HasText(t.Path))
                {
                    Logging.Warn("  ✖ " + Path.GetFileName(path) + "  →  解析不出目标，已跳过");
                    return false;
                }
                target = t.Path;
                args = t.Arguments;
                cwd = t.WorkingDir;
                source = "快捷方式";
                name = Path.GetFileNameWithoutExtension(path);
                Logging.Ok("  ✔ " + Path.GetFileName(path) + "  →  " + target);
            }
            else if (Directory.Exists(path))
            {
                target = path;
                source = "文件夹";
                name = new DirectoryInfo(path).Name;
                Logging.Ok("  ✔ " + name + "  →  " + path + "（文件夹）");
            }
            else if (File.Exists(path))
            {
                target = path;
                name = Path.GetFileNameWithoutExtension(path);
                Logging.Ok("  ✔ " + Path.GetFileName(path));
            }
            else
            {
                Logging.Warn("  ✖ " + path + "  →  路径不存在，已跳过");
                return false;
            }

            foreach (AppEntry ex in _cfg.Entries)
                if (Helper.SamePath(ex.Target, target))
                {
                    Logging.Warn("  · 「" + ex.Name + "」已经在列表里了，跳过重复添加。");
                    return false;
                }

            var e = new AppEntry();
            e.Id = ConfigStore.NextId(_cfg);
            e.Name = Helper.HasText(name) ? name : Helper.ExeName(target);
            e.Target = target;
            e.Arguments = args;
            e.WorkingDir = Helper.HasText(cwd) ? cwd : Helper.DirName(target);
            e.Source = source;
            e.DelaySeconds = _cfg.DefaultDelaySeconds;
            e.Group = DefaultGroupKey();
            e.Enabled = enable;
            e.IsNew = false;

            string exePath = Helper.ExtractExePath(target);
            if (Helper.FileExistsSafe(exePath) && Launcher.RequiresAdmin(exePath))
                e.Note = "可能需要管理员权限";

            // 记住原始 .lnk：启动时优先按"双击快捷方式"处理（保留参数 / 起始位置 / AppUserModelID）
            if (ext == ".lnk" || ext == ".url") e.ShortcutPath = path;

            _cfg.Entries.Add(e);
            _detail = e;
            return true;
        }

        private string DefaultGroupKey()
        {
            if (_selectedGroup != AllGroupKey && _selectedGroup != FavGroupKey) return _selectedGroup;
            if (_cfg.Groups.Count > 0) return _cfg.Groups[0].Key;
            return "daily";
        }

        // =========================================================== 启动序列

        private void StartSequence(List<AppEntry> items, string label)
        {
            if (_running)
            {
                Logging.Warn("上一轮启动还没结束，先点「停止」再开始新的。");
                return;
            }
            if (items.Count == 0)
            {
                Logging.Warn("没有可启动的程序（一个都没勾）。");
                return;
            }

            _running = true;
            _stopRequested = false;
            _runIndex = 0;
            _runTotal = items.Count;
            _runStarted = Stopwatch.GetTimestamp();

            foreach (AppEntry item in items)
            {
                RowState removed;
                _states.TryRemove(item.Id, out removed);
            }

            _btnStop.Enabled = true;
            _btnRun.Enabled = false;
            _lblFooterStat.Text = label + " · 0/" + items.Count;
            _lblFooterStat.ForeColor = Theme.Ac2;
            RefreshList();

            Logging.Info("══════════════════════════════════════");
            Logging.Info(string.Format("【{0}】开始，共 {1} 个程序，预计 {2}",
                label, items.Count, Helper.HumanSeconds(SumDelay(items))));
            Logging.Info("等待策略：串行，每个程序之后等它自己的延迟秒数");

            var t = new Thread(delegate() { RunWorker(items, label); });
            t.IsBackground = true;
            t.Name = "QuickLaunch-Runner";
            t.Start();
        }

        private static int SumDelay(List<AppEntry> items)
        {
            int s = 0;
            for (int i = 0; i < items.Count - 1; i++) s += items[i].DelaySeconds;
            return s;
        }

        private void RunWorker(List<AppEntry> items, string label)
        {
            int total = items.Count;
            for (int i = 0; i < total; i++)
            {
                if (_stopRequested) break;

                AppEntry e = items[i];
                int index = i + 1;
                _runIndex = index;

                SetState(e, 1, "启动中…");
                UpdateFooterStatThreadSafe(label, index - 1, total);
                Logging.Info(string.Format("[{0}/{1}] 启动 {2}", index, total, e.Name));

                if (_cfg.SkipRunning && Launcher.IsRunning(e.Target))
                {
                    SetState(e, 4, "已在运行，跳过");
                    Logging.Warn("  ⏭ 「" + e.Name + "」已经在运行了，跳过（可能只是最小化到了托盘）。");
                    Logging.Info("      想强制重新打开：先把这个程序彻底退出（含托盘图标右键退出），再点一次。");
                    Logging.Info("      不想有这个行为：右侧关掉「已在运行时跳过不重复开」。");
                }
                else
                {
                    LaunchResult r;
                    try { r = Launcher.Launch(e, _cfg.FoldersInExplorer); }
                    catch (Exception ex)
                    {
                        Logging.Exception("启动「" + e.Name + "」", ex);
                        r = LaunchResult.Fail("异常：" + ex.Message);
                    }

                    if (r.Ok)
                    {
                        SetState(e, 2, "PID " + r.ProcessId);
                        Logging.Ok(string.Format("  ✔ 已启动    PID {0}    用时 {1} ms", r.ProcessId, r.ElapsedMs));
                        if (r.NeedsAdmin) Logging.Info("  （该程序通过 UAC 提权启动）");
                    }
                    else if (r.UserCancelled)
                    {
                        SetState(e, 3, "UAC 被取消");
                        Logging.Warn("  ⚠ UAC 授权被取消，这个程序没启动，继续下一个。");
                    }
                    else
                    {
                        SetState(e, 3, "启动失败");
                        Logging.Err("  ✖ 启动失败：" + r.Message);
                    }
                }

                UpdateFooterStatThreadSafe(label, index, total);

                if (i >= total - 1) continue;
                int wait = e.DelaySeconds;
                if (wait <= 0) continue;

                Logging.Write(LogLevel.WAIT, string.Format("  ⏳ 等待 {0} 秒，避免同时抢占磁盘和 CPU…", wait));
                int elapsed = 0;
                while (elapsed < wait * 1000)
                {
                    if (_stopRequested) break;
                    int slice = Math.Min(100, wait * 1000 - elapsed);
                    Thread.Sleep(slice);
                    elapsed += slice;
                }
                if (_stopRequested) break;
            }

            bool stopped = _stopRequested;
            _stopRequested = false;
            _running = false;

            long ms = (long)((Stopwatch.GetTimestamp() - _runStarted) * 1000.0 / Stopwatch.Frequency);

            if (stopped)
                Logging.Warn(string.Format("【{0}】已被中断：完成 {1}/{2}，用时 {3} 秒。已启动的进程不会自动关闭。",
                    label, _runIndex, total, (ms / 1000.0).ToString("0.0")));
            else
                Logging.Ok(string.Format("【{0}】全部完成：{1} 个程序，总用时 {2} 秒。",
                    label, total, (ms / 1000.0).ToString("0.0")));
            Logging.Info("日志文件：" + Logging.FilePath);

            try
            {
                BeginInvoke(new MethodInvoker(delegate()
                {
                    _lblFooterStat.Text = stopped ? "已中断" : "就绪";
                    _lblFooterStat.ForeColor = stopped ? Theme.Warn : Theme.Tx2;
                    UpdateRunStats();
                    RefreshList();
                }));
            }
            catch { }
        }

        private void SetState(AppEntry e, int status, string message)
        {
            var st = new RowState();
            st.Status = status;
            st.Message = message;
            _states[e.Id] = st;

            try
            {
                if (IsHandleCreated && InvokeRequired)
                    BeginInvoke(new MethodInvoker(delegate { _list.SetItemStatus(e, status, message); }));
                else
                    _list.SetItemStatus(e, status, message);
            }
            catch { }
        }

        private void UpdateFooterStatThreadSafe(string label, int done, int total)
        {
            try
            {
                if (!IsHandleCreated) return;
                BeginInvoke(new MethodInvoker(delegate()
                {
                    _lblFooterStat.Text = string.Format("{0} · {1}/{2}", label, done, total);
                }));
            }
            catch { }
        }

        private void OnClickUpdatePaths(object sender, EventArgs e)
        {
            Logging.Info("开始核对路径：检查每个程序的目标文件还在不在…");
            var t = new Thread(delegate()
            {
                int ok = 0, bad = 0;
                foreach (AppEntry entry in new List<AppEntry>(_cfg.Entries))
                {
                    if (Helper.LooksLikeShellTarget(entry.Target) || Helper.LooksLikeUrl(entry.Target)) { ok++; continue; }
                    if (Directory.Exists(entry.Target) || File.Exists(entry.Target)) { ok++; continue; }

                    bad++;
                    Logging.Warn("  ✖ 找不到：" + entry.Name + "  →  " + entry.Target);
                }

                Logging.Ok(string.Format("核对完成：正常 {0} 个，失效 {1} 个。", ok, bad));
                if (bad > 0)
                    Logging.Warn("失效的可能是被卸载或改名了：点 ✕ 移除，再把新的快捷方式拖进来即可。");

                try { BeginInvoke(new MethodInvoker(delegate { RefreshList(); })); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        // =========================================================== 批量勾选

        private void OnClickCheckAllVisible(object sender, EventArgs e)
        {
            if (_list.Count == 0)
            {
                Logging.Warn("当前列表是空的，没有可以勾选的行。");
                return;
            }

            bool allOn = true;
            foreach (AppListView.Item it in _list.Items)
                if (!it.Entry.Enabled) { allOn = false; break; }

            int n = 0;
            foreach (AppListView.Item it in _list.Items)
            {
                it.Entry.Enabled = !allOn;
                if (!allOn && it.Entry.IsNew) it.Entry.IsNew = false;
                n++;
            }

            MarkDirty();
            Logging.Ok(string.Format("{0} 当前列表里的 {1} 个程序。", allOn ? "已取消勾选" : "已勾选", n));
            RefreshList();
            UpdateDetailFromSelection();
        }

        // =========================================================== 配置存取

        internal void MarkDirty()
        {
            _dirty = true;
            _btnSave.Glyph = "\uE74E";
            _btnSave.Invalidate();
        }

        /// <summary>
        /// 给命令行自检模式灌一批测试条目。
        /// 本程序不再扫描系统，所以测试数据是合成的（见 Program.BuildTestEntries）。
        /// </summary>
        internal void LoadTestData(List<AppEntry> list, bool enableFirstFew)
        {
            foreach (AppEntry d in list)
            {
                d.Id = ConfigStore.NextId(_cfg);
                d.DelaySeconds = _cfg.DefaultDelaySeconds;
                d.Group = DefaultGroupKey();
                d.Enabled = false;
                d.IsNew = true;
                _cfg.Entries.Add(d);
            }

            if (enableFirstFew)
            {
                int n = 0;
                foreach (AppEntry e in _cfg.Entries)
                {
                    if (n >= 3) break;
                    if (Helper.LooksLikeShellTarget(e.Target)) continue;
                    e.Enabled = true;
                    e.DelaySeconds = 2 + n;
                    n++;
                }
            }

            MarkDirty();
            _dirty = true;
            _configSaved = false;
            SaveConfig(false);
        }

        private void SaveConfig(bool notify)
        {
            string err;
            bool ok = ConfigStore.Save(_cfg, out err);
            if (ok)
            {
                _dirty = false;
                _configSaved = true;
                _btnSave.Glyph = "\uE73E";
                _btnSave.Invalidate();
                if (notify) Logging.Ok(string.Format("配置已保存：{0}（{1} 项）", Helper.ConfigPath, _cfg.Entries.Count));
                else Logging.Info("配置已保存（退出前自动保存）。");
            }
            else
            {
                Logging.Err("配置保存失败：" + err);
                if (notify) MessageBox.Show("保存失败：" + err, "一键启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnClickReload(object sender, EventArgs e)
        {
            DialogResult r = MessageBox.Show("重新从磁盘读取配置？当前未保存的改动会丢失。",
                "重载配置", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            AppConfig fresh = ConfigStore.Load();
            _cfg.Entries.Clear();
            _cfg.Entries.AddRange(fresh.Entries);
            _cfg.Groups.Clear();
            _cfg.Groups.AddRange(fresh.Groups);
            _dirty = false;
            _detail = null;
            _states.Clear();

            SyncConfigToSwitches();
            RebuildGroups();
            RefreshList();
            UpdateDetailFromSelection();
            Logging.Ok("配置已重新载入。");
        }

        private void SyncConfigToSwitches()
        {
            _swSerial.SetOn(_cfg.SerialLaunch);
            _swSkip.SetOn(_cfg.SkipRunning);
            _swExplorer.SetOn(_cfg.FoldersInExplorer);

        }

        private void SyncSwitchesToConfig()
        {
            _cfg.SerialLaunch = _swSerial.TagIsOn;
            _cfg.SkipRunning = _swSkip.TagIsOn;
            _cfg.FoldersInExplorer = _swExplorer.TagIsOn;

            MarkDirty();
        }

        private void OnClickLogFilter(object sender, EventArgs e)
        {
            var b = sender as QButton;
            if (b == null) return;

            bool turningOff = b.TagIsOn;
            foreach (Control c in b.Parent.Controls)
            {
                var q = c as QButton;
                if (q == null || !(q.Tag is LogLevel?)) continue;
                q.TagIsOn = false;
                q.Invalidate();
            }

            if (turningOff) { _logView.SetFilter(null); return; }

            b.TagIsOn = true;
            b.Invalidate();
            _logView.SetFilter((LogLevel?)b.Tag);
        }

        private void OnClickOpenLogs(object sender, EventArgs e)
        {
            try
            {
                string dir = Logging.LogFolder;
                Directory.CreateDirectory(dir);

                // 同样交给 shell 打开，不手动 spawn explorer.exe
                LaunchResult r = Launcher.OpenInExplorer(dir);
                if (r.Ok) Logging.Info("已打开日志文件夹：" + dir);
                else Logging.Err("打开日志文件夹失败：" + r.Message);
            }
            catch (Exception ex) { Logging.Err("打开日志文件夹失败：" + ex.Message); }
        }

        private void ToggleLightTheme()
        {
            Logging.Info("配色目前固定为「中深灰蓝」。改色请改 CustomControls.cs 里的 Theme 颜色常量后重新编译。");
        }

        // =========================================================== 小对话框

        private string Prompt(string title, string caption, string def)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(Theme.S(420), Theme.S(158));
                f.BackColor = Theme.Bg;
                f.ForeColor = Theme.Tx;
                f.Font = Theme.UiFont(10f);
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.AutoScaleMode = AutoScaleMode.None;

                var lbl = new Label();
                lbl.Text = caption;
                lbl.ForeColor = Theme.Tx2;
                lbl.Location = new Point(Theme.S(16), Theme.S(14));
                lbl.Size = new Size(Theme.S(388), Theme.S(44));

                var box = new TextBox();
                box.Text = def;
                box.Location = new Point(Theme.S(16), Theme.S(62));
                box.Width = Theme.S(388);
                box.BackColor = Theme.Bg3;
                box.ForeColor = Theme.Tx;
                box.BorderStyle = BorderStyle.FixedSingle;

                var ok = new QButton("确定", QButton.Look.Primary);
                ok.Location = new Point(Theme.S(224), Theme.S(110));
                ok.Size = new Size(Theme.S(88), Theme.S(32));
                ok.Click += delegate { f.DialogResult = DialogResult.OK; f.Close(); };

                var cancel = new QButton("取消", QButton.Look.Normal);
                cancel.Location = new Point(Theme.S(318), Theme.S(110));
                cancel.Size = new Size(Theme.S(86), Theme.S(32));
                cancel.Click += delegate { f.DialogResult = DialogResult.Cancel; f.Close(); };

                f.Controls.Add(lbl);
                f.Controls.Add(box);
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = null;

                box.SelectAll();
                box.Focus();

                return f.ShowDialog(this) == DialogResult.OK ? box.Text : "";
            }
        }

        // =========================================================== 无边框窗口缩放

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
            const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if (m.Result != IntPtr.Zero) return;

                int x = unchecked((short)(long)m.LParam);
                int y = unchecked((short)((long)m.LParam >> 16));
                Point p = PointToClient(new Point(x, y));
                int g = Theme.S(7);

                bool left = p.X <= g, right = p.X >= ClientSize.Width - g;
                bool top = p.Y <= g, bottom = p.Y >= ClientSize.Height - g;

                if (top && left) m.Result = (IntPtr)HTTOPLEFT;
                else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
                else if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (bottom && right) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                return;
            }

            base.WndProc(ref m);
        }
    }
}
