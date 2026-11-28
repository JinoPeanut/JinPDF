using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class MainWindow : Form, IMessageFilter
{
    const int MouseWheelMessage = 0x020a;
    const int ControlKeyInMouseMessage = 0x0008;
    readonly EngineClient preview = new EngineClient();
    readonly EngineClient thumbs = new EngineClient();
    readonly EngineClient conversion = new EngineClient();
    readonly EngineClient printing = new EngineClient();
    readonly PdfTabs tabs = new PdfTabs { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed,
        SizeMode = TabSizeMode.Fixed, ItemSize = new Size(167, 34), ShowToolTips = true, HotTrack = true };
    readonly Button print = Theme.Button("인쇄");
    readonly FlowLayoutPanel printMarginOptions = new FlowLayoutPanel { AutoSize = true, WrapContents = false,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0) };
    readonly CheckBox customPrintMargins = new CheckBox { Text = "인쇄 여백", AutoSize = true,
        Margin = new Padding(0, 3, 4, 0) };
    readonly NumericUpDown printMarginMillimeters = new NumericUpDown { AccessibleName = "인쇄 여백 (mm)",
        Minimum = 1, Maximum = 50, Value = 15, Width = 48, Enabled = false, Margin = new Padding(0, 0, 4, 0) };
    readonly Button compare = Theme.Button("나란히 보기");
    readonly ContextMenuStrip explorerMenu = new ContextMenuStrip();
    readonly Button explorer = Theme.Button("탐색기 메뉴");
    readonly string sendToFolder = Environment.GetFolderPath(Environment.SpecialFolder.SendTo, Environment.SpecialFolderOption.DoNotVerify);
    readonly Label message = Theme.Text("");
    readonly Label printMessage = Theme.Text("");
    readonly RowStyle footerHeight = new RowStyle(SizeType.Absolute, 0);
    readonly ProgressBar printProgress = new ProgressBar { Width = 156, Height = 13, Visible = false };
    readonly MergeView merge;
    readonly TabPage mergeTab = new TabPage("파일 병합");
    readonly string temporary;
    readonly HashSet<string> opening = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    bool isPrinting;
    bool editing;
    static readonly StringComparison FileComparison = StringComparison.OrdinalIgnoreCase;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainWindow(args));
    }

    public MainWindow(string[] args)
    {
        Text = "JinPDF";
        using (var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("JinPDF.ico"))
            if (resource != null)
                using (var icon = new Icon(resource)) Icon = (Icon)icon.Clone();
        Font = new Font("Malgun Gothic", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 540);
        ClientSize = new Size(1130, 730);
        BackColor = Theme.Surface;
        var workRoot = Environment.GetEnvironmentVariable("JINPDF_TEMP") ?? Environment.GetEnvironmentVariable("PDFMATE_TEMP");
        temporary = Path.Combine(string.IsNullOrEmpty(workRoot) ? Path.GetTempPath() : workRoot,
            "JinPDF-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        AllowDrop = true;
        DragEnter += delegate(object sender, DragEventArgs e) {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        };
        DragDrop += delegate(object sender, DragEventArgs e) {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null) return;
            if (tabs.SelectedTab == mergeTab || paths.Any(p => !Path.GetExtension(p).Equals(".pdf", FileComparison)))
            { tabs.SelectedTab = mergeTab; merge.AddFiles(paths); }
            else foreach (string path in paths) OpenPdf(path);
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(10, 0, 10, 0), BackColor = Theme.Surface };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(footerHeight);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(12, 0, 12, 0), BackColor = Theme.Surface };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var identity = Theme.Row();
        identity.BackColor = Theme.Surface;
        identity.Padding = new Padding(0, 8, 0, 0);
        var mark = new Panel { Size = new Size(29, 31), Margin = new Padding(0, 2, 9, 0) };
        mark.Paint += delegate(object sender, PaintEventArgs e) {
            if (Icon != null) e.Graphics.DrawIcon(Icon, new Rectangle(0, 0, 27, 27));
        };
        var brand = Theme.Text("JinPDF", 19, true);
        brand.Margin = Padding.Empty;
        identity.Controls.AddRange(new Control[] { mark, brand });
        header.Controls.Add(identity, 0, 0);
        var commands = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        var open = Theme.Button("PDF 열기");
        open.Margin = Padding.Empty;
        print.Margin = new Padding(0, 0, 8, 0);
        compare.Margin = new Padding(0, 0, 8, 0);
        open.Click += delegate { Browse(); };
        print.Click += delegate { PrintActive(); };
        compare.Click += delegate { OpenComparison(); };
        explorer.Name = "ExplorerMenu";
        explorer.Click += delegate {
            RefreshExplorerMenu(sendToFolder);
            explorerMenu.Show(explorer, new Point(0, explorer.Height));
        };
        explorerMenu.Items.Add("JinPDF로 병합 등록", null, delegate { SetExplorerMenu(true); });
        explorerMenu.Items.Add("JinPDF로 병합 등록 해제", null, delegate { SetExplorerMenu(false); });
        var marginUnit = Theme.Text("mm");
        marginUnit.Margin = new Padding(0, 3, 0, 0);
        printMarginOptions.Controls.AddRange(new Control[] { customPrintMargins, printMarginMillimeters, marginUnit });
        customPrintMargins.CheckedChanged += delegate { printMarginMillimeters.Enabled = customPrintMargins.Checked; };
        commands.Controls.AddRange(new Control[] { open, print, printMarginOptions, compare, explorer });
        header.Controls.Add(commands, 1, 0);
        layout.Controls.Add(header, 0, 0);

        merge = new MergeView(conversion, temporary);
        merge.Completed += path => OpenPdf(path);
        mergeTab.Controls.Add(merge);
        tabs.TabPages.Add(mergeTab);
        tabs.DrawItem += DrawTab;
        tabs.CloseRequested += CloseTab;
        tabs.SelectedIndexChanged += delegate { UpdateTools(); };
        layout.Controls.Add(tabs, 0, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, RowCount = 1, ColumnCount = 3 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        message.AutoSize = false;
        message.AutoEllipsis = true;
        message.Dock = DockStyle.Fill;
        message.ForeColor = Theme.Muted;
        message.TextAlign = ContentAlignment.MiddleLeft;
        printMessage.Anchor = AnchorStyles.Left;
        printProgress.Anchor = AnchorStyles.Left;
        footer.Controls.Add(message, 0, 0);
        footer.Controls.Add(printMessage, 1, 0);
        footer.Controls.Add(printProgress, 2, 0);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
        UpdateTools();
        Shown += delegate {
            Application.AddMessageFilter(this);
            RefreshExplorerMenu(sendToFolder);
            if (args.Length > 0 && args[0] == ExplorerMergeMenu.MergeArgument)
                merge.AddFiles(args.Skip(1));
            else
                foreach (var path in args) OpenPdf(path);
        };
    }

    void RefreshExplorerMenu(string folder)
    {
        try
        {
            if (string.IsNullOrEmpty(folder)) throw new IOException("Windows 보내기 폴더를 찾을 수 없습니다.");
            bool exists = File.Exists(ExplorerMergeMenu.ShortcutPath(folder));
            bool current = exists && ExplorerMergeMenu.IsRegisteredFor(folder, Application.ExecutablePath);
            explorer.Text = "탐색기 메뉴 · " + (current ? "등록됨" : exists ? "다시 등록 필요" : "미등록");
            explorerMenu.Items[0].Enabled = !current;
            explorerMenu.Items[1].Enabled = exists;
        }
        catch (Exception)
        {
            explorer.Text = "탐색기 메뉴 · 확인 불가";
            explorerMenu.Items[0].Enabled = true;
            explorerMenu.Items[1].Enabled = true;
        }
    }

    void SetExplorerMenu(bool register)
    {
        try
        {
            if (string.IsNullOrEmpty(sendToFolder)) throw new IOException("Windows 보내기 폴더를 찾을 수 없습니다.");
            if (register)
            {
                ExplorerMergeMenu.Register(sendToFolder, Application.ExecutablePath);
                Status("탐색기 메뉴 등록 완료. 우클릭 → 보내기 → JinPDF로 병합을 선택하세요.");
            }
            else
            {
                ExplorerMergeMenu.Unregister(sendToFolder);
                Status("탐색기 메뉴에서 JinPDF로 병합 등록을 해제했습니다.");
            }
        }
        catch (Exception error) { Status("탐색기 메뉴를 변경할 수 없습니다. " + error.Message); }
        finally { RefreshExplorerMenu(sendToFolder); }
    }

    DocumentView Active { get { return tabs.SelectedTab == null ? null : tabs.SelectedTab.Tag as DocumentView; } }
    IEnumerable<DocumentView> Docs { get { return tabs.TabPages.Cast<TabPage>().Select(t => t.Tag).OfType<DocumentView>(); } }

    void DrawTab(object sender, DrawItemEventArgs e)
    {
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool canClose = tabs.TabPages[e.Index] != mergeTab;
        var foreground = Theme.Ink;
        using (var background = new SolidBrush(selected ? Theme.Tint : Color.White)) e.Graphics.FillRectangle(background, e.Bounds);
        if (selected)
            using (var indicator = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(indicator, e.Bounds.Left + 1, e.Bounds.Bottom - 3, e.Bounds.Width - 2, 3);
        var label = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top, e.Bounds.Width - (canClose ? 41 : 20), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Font, label, foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (canClose)
            TextRenderer.DrawText(e.Graphics, "×", Font, PdfTabs.CloseBounds(e.Bounds), foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    void UpdateTools()
    {
        print.Enabled = Active != null && !isPrinting;
        compare.Enabled = Active != null && Docs.Count() >= 2;
        print.Visible = Active != null;
        printMarginOptions.Visible = print.Visible;
        printMarginOptions.Enabled = !isPrinting;
        compare.Visible = compare.Enabled;
        UpdateFooter();
        if (Active != null) Active.ActivateView();
        tabs.Invalidate();
    }

    void Status(string value) { message.Text = value; UpdateFooter(); }

    void UpdateFooter()
    {
        footerHeight.Height = tabs.SelectedTab != mergeTab || isPrinting || !string.IsNullOrEmpty(printMessage.Text) ||
            message.Text.StartsWith("PDF를 열 수 없습니다.", StringComparison.Ordinal) ||
            message.Text.StartsWith("탐색기 메뉴", StringComparison.Ordinal) ? 30 : 0;
    }

    void Browse()
    {
        using (var dialog = new OpenFileDialog { Multiselect = true, Filter = "PDF 문서|*.pdf" })
            if (dialog.ShowDialog(this) == DialogResult.OK)
                foreach (var path in dialog.FileNames) OpenPdf(path);
    }

    async void OpenPdf(string filename)
    {
        string path = Path.GetFullPath(filename);
        foreach (TabPage tab in tabs.TabPages)
        {
            var existing = tab.Tag as DocumentView;
            if (existing != null && existing.FilePath.Equals(path, FileComparison)) { tabs.SelectedTab = tab; return; }
        }
        if (!opening.Add(path)) return;
        Status("문서 여는 중 · " + Path.GetFileName(path));
        try
        {
            int count = await Task.Run(() => (int)preview.Run(new { op = "count", path = path })["count"]);
            if (IsDisposed) return;
            var view = new DocumentView(path, count, preview, thumbs, temporary, Status, true, 0);
            view.DeleteRequested += delegate { DeletePages(view); };
            AddTab(Path.GetFileName(path), path, view, view);
            Status(path);
        }
        catch (Exception error) { if (!IsDisposed) Status("PDF를 열 수 없습니다. " + error.Message); }
        finally { opening.Remove(path); }
    }

    void AddTab(string name, string tooltip, Control content, DocumentView view = null)
    {
        var tab = new TabPage(name) { ToolTipText = tooltip, Tag = view, BackColor = Color.White };
        tab.Controls.Add(content);
        tabs.TabPages.Add(tab);
        tabs.SelectedTab = tab;
        UpdateTools();
    }

    async void DeletePages(DocumentView view)
    {
        if (editing) return;
        int[] selected = view.SelectedPages.ToArray();
        if (selected.Length == 0) { Status("왼쪽 썸네일에서 삭제할 페이지를 체크하세요."); return; }
        if (selected.Length >= view.Count) { Status("최소 한 페이지를 남겨주세요."); return; }
        if (MessageBox.Show(this, selected.Length + "쪽을 삭제한 새 PDF를 저장할까요?", "선택 페이지 삭제",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        editing = true;
        Status("편집한 페이지를 새 파일로 저장하는 중…");
        try
        {
            var result = await Task.Run(() => conversion.Run(new { op = "delete", path = view.FilePath, pages = selected }));
            if (!IsDisposed) OpenPdf((string)result["path"]);
        }
        catch (Exception error) { if (!IsDisposed) Status("저장할 수 없습니다. " + error.Message); }
        finally { editing = false; }
    }

    void OpenComparison()
    {
        var left = Active;
        if (left == null) return;
        var others = Docs.Where(view => view != left).ToList();
        if (others.Count == 0) return;
        using (var dialog = new Form { Text = "나란히 볼 PDF", ClientSize = new Size(378, 113),
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            Font = Font, MinimizeBox = false, MaximizeBox = false })
        {
            var selection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(16, 15, 346, 30) };
            selection.Items.AddRange(others.Select(doc => (object)doc.FilePath).ToArray());
            selection.SelectedIndex = 0;
            var show = Theme.Button("나란히 보기", true);
            show.Location = new Point(16, 61);
            show.DialogResult = DialogResult.OK;
            dialog.Controls.AddRange(new Control[] { selection, show });
            dialog.AcceptButton = show;
            if (dialog.ShowDialog(this) == DialogResult.OK) ShowComparison(left, others[selection.SelectedIndex]);
        }
    }

    void ShowComparison(DocumentView left, DocumentView right)
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, BackColor = Theme.Line };
        var first = ComparisonPane(left, split.Panel1);
        var second = ComparisonPane(right, split.Panel2);
        AddTab("나란히 보기", left.FilePath + " | " + right.FilePath, split);
        split.SplitterDistance = split.Width / 2;
        first.ActivateView();
        second.ActivateView();
    }

    DocumentView ComparisonPane(DocumentView source, Control host)
    {
        var pane = new DocumentView(source.FilePath, source.Count, preview, thumbs, temporary, Status, false, source.Page);
        var name = Theme.Text(Path.GetFileName(source.FilePath), 10, true);
        name.AutoSize = false;
        name.AutoEllipsis = true;
        name.Dock = DockStyle.Top;
        name.Height = 30;
        name.Padding = new Padding(12, 6, 0, 0);
        name.BackColor = Color.White;
        host.Controls.Add(pane);
        host.Controls.Add(name);
        return pane;
    }

    void CloseTab(TabPage tab)
    {
        if (tab == null || tab == mergeTab) return;
        var active = tabs.SelectedTab;
        tabs.TabPages.Remove(tab);
        if (active != tab && active != null) tabs.SelectedTab = active;
        tab.Dispose();
        UpdateTools();
    }

    async void PrintActive()
    {
        var view = Active;
        if (view == null || isPrinting) return;
        try
        {
            using (var document = PrintJobs.Create(view.FilePath, view.Count))
            using (var dialog = new PrintDialog { Document = document, UseEXDialog = true, AllowSomePages = true,
                AllowSelection = false, AllowCurrentPage = false, AllowPrintToFile = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var settings = document.PrinterSettings;
                bool virtualPdf = settings.PrinterName.IndexOf("Microsoft Print to PDF", FileComparison) >= 0;
                if (virtualPdf || settings.PrintToFile)
                {
                    using (var save = new SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(view.FilePath) + "_인쇄",
                        Filter = virtualPdf ? "PDF|*.pdf" : "프린터 파일|*.prn", OverwritePrompt = true })
                    {
                        if (save.ShowDialog(this) != DialogResult.OK) return;
                        settings.PrintToFile = true;
                        settings.PrintFileName = save.FileName;
                    }
                }
                isPrinting = true;
                UpdateTools();
                printProgress.Value = 0;
                printProgress.Maximum = settings.PrintRange == PrintRange.SomePages ? settings.ToPage - settings.FromPage + 1 : view.Count;
                printProgress.Visible = true;
                printMessage.Text = "인쇄 준비 중…";
                var progress = new Progress<int>(done => {
                    if (IsDisposed || !isPrinting) return;
                    printProgress.Value = Math.Min(done, printProgress.Maximum);
                    printMessage.Text = string.Format("인쇄 중 · {0}/{1}쪽 전송", done, printProgress.Maximum);
                });
                int? marginMillimeters = customPrintMargins.Checked ? (int?)printMarginMillimeters.Value : null;
                await Task.Run(() => PrintJobs.Send(document, view.FilePath, view.Count, printing, temporary, progress, marginMillimeters));
                printMessage.Text = "인쇄 큐 전송 완료";
            }
        }
        catch (Exception error) { Status("인쇄할 수 없습니다. " + error.Message); printMessage.Text = "인쇄 오류"; }
        finally
        {
            isPrinting = false;
            printProgress.Visible = false;
            UpdateTools();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys key)
    {
        switch (key)
        {
            case Keys.Control | Keys.O: Browse(); return true;
            case Keys.Control | Keys.W: CloseTab(tabs.SelectedTab); return true;
            case Keys.Control | Keys.P: if (print.Enabled) PrintActive(); return true;
            case Keys.Control | Keys.Shift | Keys.Left:
            case Keys.Control | Keys.Shift | Keys.Right:
                var rotatingDocument = Active;
                if (rotatingDocument == null) break;
                rotatingDocument.RotatePage((key & Keys.KeyCode) == Keys.Left ? -1 : 1);
                return true;
            case Keys.Add:
            case Keys.Oemplus:
            case Keys.Shift | Keys.Oemplus:
            case Keys.Subtract:
            case Keys.OemMinus:
                var zoomDocument = Active;
                if (zoomDocument == null) break;
                zoomDocument.ChangeZoom(key == Keys.Subtract || key == Keys.OemMinus ? -1 : 1);
                return true;
            case Keys.Left:
            case Keys.Up:
            case Keys.Right:
            case Keys.Down:
                var document = Active;
                if (document == null || Control.FromChildHandle(msg.HWnd) is ComboBox) break;
                document.Navigate(document.Page + (key == Keys.Left || key == Keys.Up ? -1 : 1));
                return true;
        }
        return base.ProcessCmdKey(ref msg, key);
    }

    bool IMessageFilter.PreFilterMessage(ref Message message)
    {
        if (message.Msg != MouseWheelMessage || (message.WParam.ToInt64() & ControlKeyInMouseMessage) == 0) return false;
        var document = Active;
        var target = Control.FromChildHandle(message.HWnd);
        if (document == null || target == null || target.FindForm() != this) return false;
        long position = message.LParam.ToInt64();
        var screenPoint = new Point(unchecked((short)position), unchecked((short)(position >> 16)));
        int delta = unchecked((short)(message.WParam.ToInt64() >> 16));
        return document.ZoomFromWheel(screenPoint, delta);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && (merge.IsWorking || editing || isPrinting))
        {
            Status("진행 중인 작업이 끝난 뒤 창을 닫을 수 있습니다.");
            e.Cancel = true;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Application.RemoveMessageFilter(this);
        explorerMenu.Dispose();
        base.OnFormClosed(e);
        tabs.Dispose();
        preview.Dispose();
        thumbs.Dispose();
        conversion.Dispose();
        printing.Dispose();
        try { Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
