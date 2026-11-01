using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DsChat
{
    /// <summary>工作区文件面板：浏览 / 打开 / 插入到输入框 / 新建 / 重命名 / 删除，并设置 AI 工具开关。</summary>
    public class WorkspaceForm : Form
    {
        private Config cfg;
        private Theme th;
        private string lastReply;

        private TextBox tDir;
        private Button btnBrowse, btnOpen, btnInsert, btnNew, btnRename, btnDelete, btnExport, btnRefresh, btnUp, btnClose;
        private CheckBox cTools, cConfirm;
        private ListView list;
        private Label lblStatus;
        private string subdir = "";

        public Config Result;
        public Action<string> OnInsert;
        public Action<Config> OnChanged;

        public WorkspaceForm(Config c, Theme theme, string lastReplyText)
        {
            cfg = c.Clone();
            th = theme;
            lastReply = lastReplyText == null ? "" : lastReplyText;

            Text = "工作区";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(880, 600);
            BackColor = th.Bg;
            ForeColor = th.Text;
            Font = new Font("Microsoft YaHei UI", 9.75f);
            DoubleBuffered = true;

            BuildUi();
            if (Math.Abs(Ui.S - 1f) > 0.01f) Scale(new SizeF(Ui.S, Ui.S));
            LoadValues();
            ApplyTheme();
            LayoutColumns();
        }

        private void BuildUi()
        {
            int x = 26;
            int w = ClientSize.Width - 52;
            int y = 18;

            Label title = new Label();
            title.Text = "工作区文件";
            title.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(x, y);
            Controls.Add(title);
            y += 44;

            Label sub = new Label();
            sub.Text = "AI 只能在下面这个目录里读写文件，越界路径会被拒绝。";
            sub.AutoSize = true;
            sub.ForeColor = th.SubText;
            sub.Location = new Point(x, y);
            Controls.Add(sub);
            y += 26;

            Label ldir = new Label();
            ldir.Text = "目录";
            ldir.AutoSize = true;
            ldir.Location = new Point(x, y + 4);
            Controls.Add(ldir);

            tDir = new TextBox();
            tDir.Location = new Point(x + 44, y);
            tDir.Size = new Size(w - 44 - 92, 26);
            tDir.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(tDir);

            btnBrowse = MakeButton("选择…", x + w - 84, y, 84);
            btnBrowse.Click += delegate { BrowseDir(); };
            y += 36;

            cTools = new CheckBox();
            cTools.Text = "允许 AI 读写工作区文件（函数调用）";
            cTools.AutoSize = true;
            cTools.Location = new Point(x, y);
            Controls.Add(cTools);

            cConfirm = new CheckBox();
            cConfirm.Text = "写入 / 删除前弹窗确认";
            cConfirm.AutoSize = true;
            cConfirm.Location = new Point(x + 300, y);
            Controls.Add(cConfirm);
            y += 30;

            list = new ListView();
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.Location = new Point(x, y);
            list.Size = new Size(w, 352);
            list.Columns.Add("名称", 480);
            list.Columns.Add("大小", 90);
            list.Columns.Add("修改时间", 160);
            list.DoubleClick += delegate { OpenSelected(); };
            Controls.Add(list);
            y += 362;

            int bx = x;
            btnOpen = MakeButton("打开", bx, y, 80); bx += 90;
            btnOpen.Click += delegate { OpenSelected(); };
            btnInsert = MakeButton("插入", bx, y, 80); bx += 90;
            btnInsert.Click += delegate { InsertSelected(); };
            btnNew = MakeButton("新建", bx, y, 80); bx += 90;
            btnNew.Click += delegate { NewFile(); };
            btnRename = MakeButton("重命名", bx, y, 80); bx += 90;
            btnRename.Click += delegate { RenameSelected(); };
            btnDelete = MakeButton("删除", bx, y, 80); bx += 90;
            btnDelete.Click += delegate { DeleteSelected(); };
            btnExport = MakeButton("导出回复", bx, y, 90); bx += 100;
            btnExport.Click += delegate { ExportReply(); };
            btnRefresh = MakeButton("刷新", bx, y, 80); bx += 90;
            btnRefresh.Click += delegate { RefreshList(); };
            btnUp = MakeButton("上级", bx, y, 80);
            btnUp.Click += delegate { GoUp(); };

            btnClose = MakeButton("关闭", ClientSize.Width - x - 80, y, 80);
            btnClose.Click += delegate { CloseAndSave(); };
            y += 40;

            lblStatus = new Label();
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(w, 20);
            lblStatus.Location = new Point(x, y);
            lblStatus.ForeColor = th.SubText;
            Controls.Add(lblStatus);
        }

        private Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = th.Border;
            b.BackColor = th.InputBg;
            b.ForeColor = th.Text;
            Controls.Add(b);
            return b;
        }

        private void LayoutColumns()
        {
            if (list == null || list.Columns.Count < 3) return;
            int total = Math.Max(300, list.ClientSize.Width - 4);
            int c2 = Ui.Px(90);
            int c3 = Ui.Px(160);
            list.Columns[0].Width = Math.Max(120, total - c2 - c3);
            list.Columns[1].Width = c2;
            list.Columns[2].Width = c3;
        }

        private void LoadValues()
        {
            tDir.Text = cfg.Workspace;
            cTools.Checked = cfg.EnableTools;
            cConfirm.Checked = cfg.ConfirmWrites;
            RefreshList();
        }

        private void BrowseDir()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择工作区目录（AI 只能在这个目录里读写）";
                d.ShowNewFolderButton = true;
                if (Directory.Exists(tDir.Text)) d.SelectedPath = tDir.Text;
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    tDir.Text = d.SelectedPath;
                    subdir = "";
                    RefreshList();
                }
            }
        }

        private void RefreshList()
        {
            list.Items.Clear();
            string root = tDir.Text.Trim();
            subdir = (subdir == null ? "" : subdir).Trim().Trim('/', '\\');
            if (root.Length == 0)
            {
                SetStatus("还没有选择工作区目录", false);
                UpdateUpButton();
                return;
            }
            if (!Directory.Exists(root))
            {
                SetStatus("目录不存在：" + root, false);
                UpdateUpButton();
                return;
            }
            try
            {
                List<WorkspaceEntry> entries = Workspace.ListEntries(root, subdir);
                foreach (WorkspaceEntry e in entries)
                {
                    ListViewItem it = new ListViewItem(e.Path.Substring(e.Path.LastIndexOf('\\') + 1));
                    it.SubItems.Add(e.IsDir ? "" : Workspace.FormatSize(e.Size));
                    it.SubItems.Add(e.Modified.ToString("yyyy-MM-dd HH:mm"));
                    it.Tag = e;
                    list.Items.Add(it);
                }
                string where = subdir.Length == 0 ? "根目录" : subdir.Replace('\\', '/');
                SetStatus("共 " + entries.Count + " 项 · " + where + (entries.Count >= Workspace.MaxListEntries ? "（已截断）" : ""), true);
                UpdateUpButton();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, false);
                UpdateUpButton();
            }
        }

        private void UpdateUpButton()
        {
            if (btnUp != null) btnUp.Enabled = subdir.Length > 0;
        }

        private void GoUp()
        {
            if (subdir.Length == 0) return;
            int i = subdir.LastIndexOf('\\');
            subdir = i < 0 ? "" : subdir.Substring(0, i);
            RefreshList();
        }

        private WorkspaceEntry Selected()
        {
            if (list.SelectedItems.Count == 0) return null;
            return list.SelectedItems[0].Tag as WorkspaceEntry;
        }

        private void OpenSelected()
        {
            WorkspaceEntry e = Selected();
            if (e == null) { SetStatus("先选中一个文件", false); return; }
            if (e.IsDir)
            {
                subdir = e.Path;
                RefreshList();
                return;
            }
            try
            {
                string full = Workspace.Resolve(cfg.Workspace, e.Path);
                Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
            }
            catch (Exception ex) { SetStatus("打开失败：" + ex.Message, false); }
        }

        private void InsertSelected()
        {
            WorkspaceEntry e = Selected();
            if (e == null) { SetStatus("先选中一个文件", false); return; }
            if (e.IsDir) { SetStatus("目录不能插入，先打开里面的文件", false); return; }
            try
            {
                string text = Workspace.ReadText(cfg.Workspace, e.Path);
                if (OnInsert != null) OnInsert(text);
                SetStatus("已插入到输入框：" + e.Path, true);
            }
            catch (Exception ex) { SetStatus(ex.Message, false); }
        }

        private void NewFile()
        {
            string initial = subdir.Length == 0 ? "新建文件.txt" : subdir.Replace('\\', '/') + "/新建文件.txt";
            string name = Prompt.Ask(this, th, "新建文件", "相对工作区的路径，例如 notes/日记.txt", initial);
            if (name == null) return;
            try
            {
                Workspace.WriteText(cfg.Workspace, name, "");
                RefreshList();
                SetStatus("已新建：" + name, true);
            }
            catch (Exception ex) { SetStatus(ex.Message, false); }
        }

        private void RenameSelected()
        {
            WorkspaceEntry e = Selected();
            if (e == null) { SetStatus("先选中一个文件", false); return; }
            string name = Prompt.Ask(this, th, "重命名", "新的相对路径", e.Path);
            if (name == null || name == e.Path) return;
            try
            {
                string from = Workspace.Resolve(cfg.Workspace, e.Path);
                string to = Workspace.Resolve(cfg.Workspace, name);
                if (e.IsDir) Directory.Move(from, to);
                else File.Move(from, to);
                RefreshList();
                SetStatus("已重命名为：" + name, true);
            }
            catch (Exception ex) { SetStatus("重命名失败：" + ex.Message, false); }
        }

        private void DeleteSelected()
        {
            WorkspaceEntry e = Selected();
            if (e == null) { SetStatus("先选中一个文件", false); return; }
            if (MessageBox.Show(this, "确定删除 " + e.Path + " ？\r\n\r\n这个操作不可撤销。", "删除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                Workspace.Delete(cfg.Workspace, e.Path);
                RefreshList();
                SetStatus("已删除：" + e.Path, true);
            }
            catch (Exception ex) { SetStatus("删除失败：" + ex.Message, false); }
        }

        private void ExportReply()
        {
            if (lastReply.Trim().Length == 0) { SetStatus("当前没有可导出的回复", false); return; }
            string name = Prompt.Ask(this, th, "导出最后一条回复", "保存到相对工作区的路径", "回复.txt");
            if (name == null) return;
            try
            {
                int n = Workspace.WriteText(cfg.Workspace, name, lastReply);
                RefreshList();
                SetStatus("已保存 " + n + " 个字符到 " + name, true);
            }
            catch (Exception ex) { SetStatus(ex.Message, false); }
        }

        private void SetStatus(string text, bool ok)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = ok ? Color.FromArgb(46, 180, 120) : th.Danger;
        }

        private void CloseAndSave()
        {
            cfg.Workspace = tDir.Text.Trim();
            cfg.EnableTools = cTools.Checked;
            cfg.ConfirmWrites = cConfirm.Checked;
            if (cfg.EnableTools && !Workspace.IsReady(cfg.Workspace))
            {
                MessageBox.Show(this, "开启 AI 文件工具前，请先选择一个存在的工作区目录。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = cfg;
            if (OnChanged != null) OnChanged(cfg);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK)
            {
                // 直接关窗口也保存目录与开关，避免用户以为设置丢了
                cfg.Workspace = tDir.Text.Trim();
                cfg.EnableTools = cTools.Checked;
                if (!cfg.EnableTools || Workspace.IsReady(cfg.Workspace))
                {
                    Result = cfg;
                    if (OnChanged != null) OnChanged(cfg);
                }
            }
            base.OnFormClosing(e);
        }

        private void ApplyTheme()
        {
            BackColor = th.Bg;
            ForeColor = th.Text;
            foreach (Control c in Controls)
            {
                if (c is Label)
                {
                    Label l = (Label)c;
                    if (l == lblStatus) continue;
                    l.ForeColor = (l.Font.Bold && l.Font.Size > 12f) ? th.Text : th.SubText;
                }
                else if (c is CheckBox)
                {
                    c.ForeColor = th.Text;
                    c.BackColor = th.Bg;
                }
                else if (c is TextBox)
                {
                    c.BackColor = th.InputBg;
                    c.ForeColor = th.Text;
                }
                else if (c is ListView)
                {
                    c.BackColor = th.InputBg;
                    c.ForeColor = th.Text;
                }
                else if (c is Button)
                {
                    Button b = (Button)c;
                    b.FlatAppearance.BorderColor = th.Border;
                    b.BackColor = th.InputBg;
                    b.ForeColor = th.Text;
                    b.UseVisualStyleBackColor = false;
                }
            }
        }
    }

    /// <summary>极简输入框（WinForms 没有内置 InputBox）。</summary>
    internal static class Prompt
    {
        public static string Ask(IWin32Window owner, Theme th, string title, string hint, string initial)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(460, 160);
                f.BackColor = th.Bg;
                f.ForeColor = th.Text;
                f.Font = new Font("Microsoft YaHei UI", 9.75f);

                Label l = new Label();
                l.Text = hint;
                l.AutoSize = false;
                l.Size = new Size(408, 20);
                l.Location = new Point(24, 18);
                l.ForeColor = th.SubText;
                f.Controls.Add(l);

                TextBox t = new TextBox();
                t.Text = initial;
                t.Location = new Point(24, 46);
                t.Size = new Size(408, 26);
                t.BorderStyle = BorderStyle.FixedSingle;
                t.BackColor = th.InputBg;
                t.ForeColor = th.Text;
                f.Controls.Add(t);

                Button ok = new Button();
                ok.Text = "确定";
                ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(240, 96);
                ok.Size = new Size(88, 30);
                ok.FlatStyle = FlatStyle.Flat;
                ok.BackColor = th.InputBg;
                ok.ForeColor = th.Text;
                ok.FlatAppearance.BorderColor = th.Border;
                f.Controls.Add(ok);

                Button cancel = new Button();
                cancel.Text = "取消";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(344, 96);
                cancel.Size = new Size(88, 30);
                cancel.FlatStyle = FlatStyle.Flat;
                cancel.BackColor = th.InputBg;
                cancel.ForeColor = th.Text;
                cancel.FlatAppearance.BorderColor = th.Border;
                f.Controls.Add(cancel);

                f.AcceptButton = ok;
                f.CancelButton = cancel;
                if (Math.Abs(Ui.S - 1f) > 0.01f) f.Scale(new SizeF(Ui.S, Ui.S));
                f.ActiveControl = t;
                t.SelectAll();
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                string v = t.Text.Trim();
                return v.Length == 0 ? null : v;
            }
        }
    }

    public enum ConfirmChoice { Deny, Allow, AllowTurn }

    /// <summary>AI 写入/删除前的确认框：允许一次、本轮全部允许、拒绝。</summary>
    internal static class Confirm
    {
        public static ConfirmChoice AskWrite(IWin32Window owner, Theme th, string path, string content)
        {
            string preview = content == null ? "" : content.Replace("\r\n", "\n").Replace("\r", "\n");
            if (preview.Length > 3000) preview = preview.Substring(0, 3000) + "\r\n…（已截断）";
            preview = preview.Replace("\n", "\r\n");
            return Ask(owner, th, "确认写入", "AI 想写入文件：", path, preview, true);
        }

        public static ConfirmChoice AskDelete(IWin32Window owner, Theme th, string path)
        {
            return Ask(owner, th, "确认删除", "AI 想删除：", path, "这个操作不可撤销。", false);
        }

        private static ConfirmChoice Ask(IWin32Window owner, Theme th, string title, string lead, string path, string detail, bool showPreview)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                int w = showPreview ? 620 : 520;
                int h = showPreview ? 420 : 220;
                f.ClientSize = new Size(w, h);
                f.BackColor = th.Bg;
                f.ForeColor = th.Text;
                f.Font = new Font("Microsoft YaHei UI", 9.75f);

                Label heading = new Label();
                heading.Text = title;
                heading.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
                heading.AutoSize = true;
                heading.Location = new Point(24, 16);
                heading.ForeColor = th.Text;
                f.Controls.Add(heading);

                Label ll = new Label();
                ll.Text = lead;
                ll.AutoSize = true;
                ll.Location = new Point(24, 54);
                ll.ForeColor = th.SubText;
                f.Controls.Add(ll);

                Label lp = new Label();
                lp.Text = path;
                lp.AutoSize = false;
                lp.Size = new Size(w - 48, 24);
                lp.Location = new Point(24, 76);
                lp.ForeColor = th.Text;
                lp.Font = new Font("Microsoft YaHei UI", 9.75f, FontStyle.Bold);
                f.Controls.Add(lp);

                if (showPreview)
                {
                    TextBox tb = new TextBox();
                    tb.Multiline = true;
                    tb.ReadOnly = true;
                    tb.TabStop = false;
                    tb.ScrollBars = ScrollBars.Vertical;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    tb.Font = new Font("Consolas", 9f);
                    tb.BackColor = th.InputBg;
                    tb.ForeColor = th.CodeText;
                    tb.SetBounds(24, 108, w - 48, h - 108 - 76);
                    tb.Text = detail;
                    f.Controls.Add(tb);
                }
                else
                {
                    Label warn = new Label();
                    warn.Text = detail;
                    warn.AutoSize = false;
                    warn.Size = new Size(w - 48, 40);
                    warn.Location = new Point(24, 112);
                    warn.ForeColor = th.Danger;
                    f.Controls.Add(warn);
                }

                ConfirmChoice choice = ConfirmChoice.Deny;
                int by = h - 52;

                Button allow = new Button();
                allow.Text = "允许";
                allow.Size = new Size(100, 32);
                allow.Location = new Point(w - 24 - 100, by);
                allow.FlatStyle = FlatStyle.Flat;
                allow.BackColor = th.Accent;
                allow.ForeColor = Color.White;
                allow.FlatAppearance.BorderColor = th.Accent;
                allow.Click += delegate { choice = ConfirmChoice.Allow; f.DialogResult = DialogResult.OK; f.Close(); };
                f.Controls.Add(allow);

                Button all = new Button();
                all.Text = "本轮全部允许";
                all.Size = new Size(150, 32);
                all.Location = new Point(allow.Left - 12 - 150, by);
                all.FlatStyle = FlatStyle.Flat;
                all.BackColor = th.InputBg;
                all.ForeColor = th.Text;
                all.FlatAppearance.BorderColor = th.Border;
                all.Click += delegate { choice = ConfirmChoice.AllowTurn; f.DialogResult = DialogResult.OK; f.Close(); };
                f.Controls.Add(all);

                Button deny = new Button();
                deny.Text = "拒绝";
                deny.Size = new Size(90, 32);
                deny.Location = new Point(all.Left - 12 - 90, by);
                deny.FlatStyle = FlatStyle.Flat;
                deny.BackColor = th.InputBg;
                deny.ForeColor = th.Text;
                deny.FlatAppearance.BorderColor = th.Border;
                deny.Click += delegate { choice = ConfirmChoice.Deny; f.DialogResult = DialogResult.Cancel; f.Close(); };
                f.Controls.Add(deny);

                f.AcceptButton = allow;
                f.CancelButton = deny;
                if (Math.Abs(Ui.S - 1f) > 0.01f) f.Scale(new SizeF(Ui.S, Ui.S));
                f.ActiveControl = allow;
                f.ShowDialog(owner);
                return choice;
            }
        }
    }
}
