using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class MergeView : UserControl
{
    static readonly string[] Extensions = { ".pdf", ".jpg", ".jpeg", ".jfif", ".png", ".xls", ".xlsx", ".ppt", ".pptx" };
    static readonly string FileFilter = "지원 파일|" + string.Join(";", Extensions.Select(extension => "*" + extension));
    readonly MergeFileGrid files = new MergeFileGrid();
    readonly Label totals = Theme.Text("", 12, true);
    readonly Label destination = Theme.Text("");
    readonly Label state = Theme.Text("");
    readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Height = 7, Visible = false, Margin = new Padding(0, 1, 12, 0) };
    readonly Button merge = Theme.Button("병합하기", true);
    readonly Button remove = Theme.Button("선택 항목 제외");
    readonly Button add = Theme.Button("파일 추가");
    readonly RowStyle headingHeight = new RowStyle(SizeType.Absolute, 44);
    readonly RowStyle dropHeight = new RowStyle(SizeType.Absolute, 65);
    readonly RowStyle actionsHeight = new RowStyle(SizeType.Absolute, 48);
    readonly RowStyle recentHeight = new RowStyle(SizeType.Absolute, 0);
    readonly Label recentSummary = Theme.Text("");
    readonly Button recentToggle = Theme.Button("파일별 쪽수 보기");
    readonly Control recentPanel;
    readonly ListView recentFiles = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HeaderStyle = ColumnHeaderStyle.None, ShowItemToolTips = true,
        Margin = new Padding(0, 3, 0, 0), AccessibleName = "최근 병합 파일별 쪽수" };
    readonly ToolTip tips = new ToolTip();
    readonly List<Entry> added = new List<Entry>();
    readonly EngineClient conversion;
    readonly string temporary;
    bool busy;
    bool recentExpanded;
    public event Action<string> Completed;
    public bool IsWorking { get { return busy; } }

    sealed class Entry
    {
        public string Path;
        public int Count;
        public DataGridViewRow Row;
    }

    public MergeView(EngineClient conversion, string temporary)
    {
        this.conversion = conversion;
        this.temporary = temporary;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
            Padding = new Padding(24, 18, 24, 0), Margin = Padding.Empty, BackColor = Color.White };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(headingHeight);
        layout.RowStyles.Add(dropHeight);
        layout.RowStyles.Add(actionsHeight);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(recentHeight);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        var heading = Theme.Text("파일 병합", 20, true);
        heading.Margin = Padding.Empty;
        layout.Controls.Add(heading, 0, 0);

        var drop = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Margin = new Padding(0, 0, 0, 8), Padding = new Padding(16, 0, 14, 0), BackColor = Theme.Surface };
        drop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));
        drop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        drop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        drop.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var dropIcon = new Panel { Size = new Size(24, 28), Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        dropIcon.Paint += delegate(object sender, PaintEventArgs e) { Theme.DrawFileIcon(e.Graphics, dropIcon.ClientRectangle, "add"); };
        var caption = Theme.Text("파일을 여기에 끌어다 놓으세요", 11);
        caption.Anchor = AnchorStyles.Left;
        caption.Margin = Padding.Empty;
        add.Anchor = AnchorStyles.Right;
        add.Margin = Padding.Empty;
        add.Click += delegate { Browse(); };
        drop.Controls.Add(dropIcon, 0, 0);
        drop.Controls.Add(caption, 1, 0);
        drop.Controls.Add(add, 2, 0);
        drop.Paint += delegate(object sender, PaintEventArgs e) { Theme.DrawBorder(e.Graphics, drop.ClientRectangle); };
        foreach (Control target in new Control[] { drop, dropIcon, caption, add, this })
        {
            target.AllowDrop = true;
            target.DragEnter += DragAllowed;
            target.DragDrop += DropFiles;
        }
        tips.SetToolTip(caption, "PDF, JPG, JFIF, PNG, Excel, PowerPoint");
        layout.Controls.Add(drop, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false,
            BackColor = Color.White, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 2, 0, 0) };
        remove.Margin = Padding.Empty;
        remove.Click += delegate { RemoveRows(files.CheckedRows()); };
        actions.Controls.Add(remove);
        layout.Controls.Add(actions, 0, 2);

        var table = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(1), BackColor = Theme.Line };
        files.ExcludeRequested += RemoveRows;
        files.FilesDropped += AddFiles;
        files.ChecksChanged += UpdateButtons;
        table.Controls.Add(files);
        layout.Controls.Add(table, 0, 3);
        recentPanel = CreateRecentMerge();
        layout.Controls.Add(recentPanel, 0, 4);
        layout.Controls.Add(CreateFooter(), 0, 5);
        Controls.Add(layout);
        SizeChanged += delegate { if (recentHeight.Height > 0) FitRecentHeight(); };
        UpdateCounts();
    }

    Control CreateRecentMerge()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2,
            Margin = new Padding(0, 8, 0, 0), Padding = new Padding(11, 3, 9, 5), BackColor = Theme.Surface, Visible = false };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Paint += delegate(object sender, PaintEventArgs e) { Theme.DrawBorder(e.Graphics, panel.ClientRectangle); };

        var heading = Theme.Text("최근 병합", 10, true);
        heading.Anchor = AnchorStyles.Left;
        heading.Margin = new Padding(0, 0, 12, 0);
        recentSummary.AutoSize = false;
        recentSummary.AutoEllipsis = true;
        recentSummary.TextAlign = ContentAlignment.MiddleLeft;
        recentSummary.Dock = DockStyle.Fill;
        recentSummary.Margin = new Padding(0, 0, 8, 0);
        recentToggle.Margin = Padding.Empty;
        recentToggle.Anchor = AnchorStyles.Right;
        recentToggle.MinimumSize = new Size(130, 35);
        recentToggle.Click += delegate { SetRecentExpanded(!recentExpanded); };
        recentFiles.Columns.Add("파일 이름");
        recentFiles.Columns.Add("쪽수", 80, HorizontalAlignment.Right);
        recentFiles.SizeChanged += delegate {
            recentFiles.Columns[0].Width = Math.Max(40, recentFiles.ClientSize.Width - 82 - SystemInformation.VerticalScrollBarWidth);
        };
        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(recentSummary, 1, 0);
        panel.Controls.Add(recentToggle, 2, 0);
        panel.Controls.Add(recentFiles, 0, 1);
        panel.SetColumnSpan(recentFiles, 3);
        return panel;
    }

    void SetRecentExpanded(bool expanded)
    {
        recentExpanded = expanded;
        FitRecentHeight();
        recentFiles.Visible = expanded;
        recentToggle.Text = expanded ? "접기" : "파일별 쪽수 보기";
    }

    void FitRecentHeight()
    {
        // Keep room for the file list and merge controls when the window is small.
        bool compact = recentExpanded && ClientSize.Height < 460;
        headingHeight.Height = compact ? 38 : 44;
        dropHeight.Height = compact ? 55 : 65;
        actionsHeight.Height = compact ? 42 : 48;
        int available = ClientSize.Height - (compact ? 311 : 333);
        recentHeight.Height = recentExpanded ? Math.Min(188, Math.Max(84, available)) : 54;
    }

    void ShowRecentMerge(string path, int pageCount, Entry[] entries)
    {
        recentSummary.Text = string.Format("{0} · 파일 {1:N0}개 · 총 {2:N0}쪽", Path.GetFileName(path), entries.Length, pageCount);
        tips.SetToolTip(recentSummary, recentSummary.Text);
        recentFiles.Items.Clear();
        recentFiles.Items.AddRange(entries.Select(entry => new ListViewItem(new[] {
            Path.GetFileName(entry.Path), string.Format("{0:N0}쪽", entry.Count) }) { ToolTipText = entry.Path }).ToArray());
        SetRecentExpanded(false);
        recentPanel.Visible = true;
    }

    Control CreateFooter()
    {
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 9, 0, 0),
            Padding = new Padding(0, 9, 0, 5), ColumnCount = 2, RowCount = 1, BackColor = Color.White };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.Paint += delegate(object sender, PaintEventArgs e) {
            using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 23, 20, 19, 8 }) summary.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        totals.Margin = Padding.Empty;
        foreach (var label in new[] { destination, state })
        {
            label.ForeColor = Theme.Muted;
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.Dock = DockStyle.Fill;
            label.Margin = new Padding(0, 0, 12, 0);
        }
        summary.Controls.Add(totals, 0, 0);
        summary.Controls.Add(destination, 0, 1);
        summary.Controls.Add(state, 0, 2);
        summary.Controls.Add(progress, 0, 3);
        footer.Controls.Add(summary, 0, 0);

        merge.Anchor = AnchorStyles.Right;
        merge.MinimumSize = new Size(172, 46);
        merge.Margin = Padding.Empty;
        merge.Click += delegate { RunMerge(); };
        footer.Controls.Add(merge, 1, 0);
        return footer;
    }

    void DragAllowed(object sender, DragEventArgs args)
    {
        args.Effect = !busy && args.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void DropFiles(object sender, DragEventArgs args)
    {
        if (args.Data.GetDataPresent(DataFormats.FileDrop)) AddFiles((string[])args.Data.GetData(DataFormats.FileDrop));
    }

    void Browse()
    {
        if (busy) return;
        using (var dialog = new OpenFileDialog { Multiselect = true, Filter = FileFilter })
            if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames);
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        if (busy) return;
        var rejected = new List<string>();
        foreach (var raw in paths)
        {
            var path = Path.GetFullPath(raw);
            if (!File.Exists(path) || !Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            { rejected.Add(Path.GetFileName(path)); continue; }
            if (added.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            var entry = new Entry { Path = path };
            entry.Row = files.AddFile(path, entry);
            added.Add(entry);
        }
        state.Text = rejected.Count > 0 ? "추가할 수 없는 파일 · " + string.Join(", ", rejected.ToArray()) : "";
        UpdateCounts();
    }

    void RemoveRows(DataGridViewRow[] rows)
    {
        if (busy || rows.Length == 0) return;
        foreach (var row in rows)
        {
            added.Remove((Entry)row.Tag);
            files.Rows.Remove(row);
        }
        state.Text = rows.Length + "개 파일을 목록에서 제외했습니다.";
        UpdateCounts();
    }

    void UpdateButtons()
    {
        add.Enabled = !busy;
        merge.Enabled = !busy && files.Rows.Count >= 2;
        remove.Enabled = !busy && files.CheckedRows().Length > 0;
    }

    void UpdateCounts()
    {
        string anchor = added.Count == 0 ? null : added[0].Path;
        int counted = added.Sum(entry => entry.Count);
        totals.Text = string.Format("파일 {0:N0}개", added.Count);
        if (counted > 0) totals.Text += string.Format("   ·   {0:N0}쪽", counted);
        destination.Text = anchor == null ? "저장 위치  처음 추가한 파일의 폴더" :
            "저장 위치  " + Path.Combine(Path.GetDirectoryName(anchor), Path.GetFileNameWithoutExtension(anchor) + "_병합.pdf");
        tips.SetToolTip(destination, destination.Text);
        UpdateButtons();
    }

    async void RunMerge()
    {
        if (!merge.Enabled) return;
        var entries = files.Rows.Cast<DataGridViewRow>().Select(row => (Entry)row.Tag).ToArray();
        var outputs = new List<string>();
        var prepared = new List<string>();
        string first = added[0].Path;
        busy = true;
        files.ReadOnly = true;
        foreach (var entry in entries)
        {
            entry.Count = 0;
            entry.Row.Cells[MergeFileGrid.State].Value = "병합 대기";
            entry.Row.Cells[MergeFileGrid.State].ToolTipText = "";
        }
        UpdateCounts();
        state.Text = "병합 중…";
        progress.Value = 0;
        progress.Maximum = entries.Length * 2 + 1;
        progress.Visible = true;
        try
        {
            for (int position = 0; position < entries.Length; position++)
            {
                var entry = entries[position];
                string output = Path.Combine(temporary, Guid.NewGuid().ToString("N") + ".pdf");
                outputs.Add(output);
                var cell = entry.Row.Cells[MergeFileGrid.State];
                cell.Value = "페이지 확인 중…";
                state.Text = string.Format("병합 준비 중 · {0}/{1} · {2}", position + 1, entries.Length, Path.GetFileName(entry.Path));
                Dictionary<string, object> ready;
                try { ready = await Task.Run(() => conversion.Run(new { op = "prepare", path = entry.Path, @out = output })); }
                catch (Exception error)
                {
                    if (!IsDisposed)
                    {
                        cell.Value = "오류 · " + error.Message;
                        cell.ToolTipText = error.Message;
                    }
                    throw;
                }
                if (IsDisposed) return;
                prepared.Add((string)ready["path"]);
                entry.Count = (int)ready["count"];
                cell.Value = "준비됨 · " + entry.Count + "쪽";
                progress.Value = position + 1;
                UpdateCounts();
            }
            state.Text = "병합 중…";
            var result = await Task.Run(() => conversion.Run(new { op = "merge", anchor = first, files = prepared.ToArray() }, info => {
                if (IsDisposed) return;
                BeginInvoke(new Action(() => {
                    if (IsDisposed || !busy) return;
                    int index = (int)info["index"];
                    progress.Value = entries.Length + (int)info["done"];
                    state.Text = index == 0 ? "병합 중 · PDF 저장 중…" : string.Format("병합 중 · {0}/{1} · {2}",
                        index, entries.Length, Path.GetFileName(entries[index - 1].Path));
                }));
            }));
            if (IsDisposed) return;
            var path = (string)result["path"];
            progress.Value = progress.Maximum;
            files.Rows.Clear();
            added.Clear();
            ShowRecentMerge(path, (int)result["count"], entries);
            state.Text = "";
            if (Completed != null) Completed(path);
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                state.Text = "병합할 수 없습니다. " + error.Message;
                tips.SetToolTip(state, state.Text);
            }
        }
        finally
        {
            foreach (string output in outputs)
            {
                try { File.Delete(output); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            busy = false;
            if (!IsDisposed) { files.ReadOnly = false; progress.Visible = false; UpdateCounts(); }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tips.Dispose();
        base.Dispose(disposing);
    }
}
