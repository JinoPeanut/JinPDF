using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

static class WindowsSmoke
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string className, string title);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    static T Find<T>(Control root, string text = null) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T && (text == null || child.Text == text)) return (T)child;
            var nested = Find<T>(child, text);
            if (nested != null) return nested;
        }
        return null;
    }

    static async Task Until(Func<bool> condition, string label)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > 50) throw new Exception("Timed out: " + label);
            await Task.Delay(25);
        }
        Console.WriteLine(label + " OK (" + watch.ElapsedMilliseconds + "ms)");
    }

    static object Invoke(Form app, string name, params object[] args)
    {
        return app.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, args);
    }

    static IEnumerable<Control> Children(Control control)
    {
        return control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c)));
    }

    static int Count(EngineClient worker, string path) { return (int)worker.Run(new { op = "count", path = path })["count"]; }

    static async Task Run(MainWindow app, string data)
    {
        VerifyButtonPaint();
        var tabs = Find<TabControl>(app);
        var merge = Find<MergeView>(app);
        var recentToggle = Find<Button>(merge, "파일별 쪽수 보기");
        var recentFiles = Find<ListView>(merge);
        if (recentToggle == null || recentFiles == null || recentToggle.Visible)
            throw new Exception("Recent results must stay hidden before the first successful merge");
        if (app.Text != "JinPDF" || Find<Label>(app, "JinPDF") == null) throw new Exception("New name must appear in the window and header");
        if (tabs.TabPages.Count != 1 || tabs.TabPages[0].Text != "파일 병합") throw new Exception("Pinned merge tab missing");
        var samples = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples");
        var initial = Directory.CreateDirectory(Path.Combine(data, "처음 추가한 곳")).FullName;
        var later = Directory.CreateDirectory(Path.Combine(data, "나머지 파일")).FullName;
        var files = Directory.GetFiles(samples).OrderBy(f => f).ToArray();
        var copies = files.Select((f, index) => Path.Combine(index == 0 ? initial : later, Path.GetFileName(f))).ToArray();
        for (int i = 0; i < files.Length; i++) File.Copy(files[i], copies[i]);
        var excludedFolder = Directory.CreateDirectory(Path.Combine(data, "목록에서 뺄 파일")).FullName;
        var excluded = new[] { "exclude-first.pdf", "exclude-second.pdf", "exclude-third.pdf" }
            .Select(name => Path.Combine(excludedFolder, name)).ToArray();
        foreach (var path in excluded) File.Copy(copies[4], path);
        merge.AddFiles(excluded.Take(1));
        merge.AddFiles(copies);
        merge.AddFiles(excluded.Skip(1));
        var save = Find<Button>(merge, "병합하기");
        var rows = Find<DataGridView>(merge);
        var exclude = Find<Button>(merge, "선택 항목 제외");
        if (save.Top <= 0 || save.PointToScreen(Point.Empty).Y <= rows.PointToScreen(Point.Empty).Y + rows.Height)
            throw new Exception("Merge button must be below the file list");
        var scrollbar = Children(rows).OfType<VScrollBar>().FirstOrDefault(control => control.Visible);
        if (scrollbar != null)
        {
            var point = scrollbar.PointToScreen(new Point(scrollbar.Width / 2, scrollbar.Height / 2));
            DropGridRow(rows, 0, point, DragDropEffects.None);
            if ((string)rows.Rows[0].Cells["File"].Value != Path.GetFileName(excluded[0]))
                throw new Exception("Dropping over the scrollbar changed the file order");
        }
        ClickGridCell(rows, "Exclude", 0);
        if (rows.Rows.Count != 7 || (string)rows.Rows[0].Cells["File"].Value != Path.GetFileName(copies[0]))
            throw new Exception("Removing from the row excluded the wrong file");
        if (exclude.Enabled) throw new Exception("Exclude action requires checked rows");
        rows.Rows[5].Cells["Check"].Value = true;
        rows.Rows[6].Cells["Check"].Value = true;
        if (!exclude.Enabled) throw new Exception("Checking rows did not enable the exclusion action");
        exclude.PerformClick();
        if (rows.Rows.Count != 5 || !excluded.All(File.Exists))
            throw new Exception("Multi-selection failed or removed original files from disk");
        Console.WriteLine("Per-row removal, multi-selection removal and untouched originals OK");
        await Task.Delay(750);
        if (!save.Enabled || rows.Rows.Count != 5 || merge.IsWorking || rows.Rows.Cast<DataGridViewRow>().Any(row => (string)row.Cells["State"].Value != "병합 대기") ||
            Directory.GetDirectories(data, "JinPDF-*").Any(folder => Directory.GetFiles(folder, "*.pdf").Length > 0))
            throw new Exception("Adding files started conversion before the merge button was clicked");
        Console.WriteLine("Adding mixed files stages them without conversion OK");
        rows.Rows[1].Cells["Check"].Value = true;
        Capture(app, Path.Combine(data, "merge-preview.png"));
        DragGridRow(rows, 0, 1);
        if ((string)rows.Rows[0].Cells["File"].Value != Path.GetFileName(copies[1]))
            throw new Exception("Dropping a row did not reorder the file list");
        rows.CurrentCell = rows.Rows[1].Cells["File"];
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rows,
            new object[] { new KeyEventArgs(Keys.Alt | Keys.Up) });
        if ((string)rows.Rows[0].Cells["File"].Value != Path.GetFileName(copies[0]))
            throw new Exception("Moving a row up did not reorder the file list");
        rows.CurrentCell = rows.Rows[0].Cells["File"];
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rows,
            new object[] { new KeyEventArgs(Keys.Alt | Keys.Down) });
        if ((string)rows.Rows[0].Cells["File"].Value != Path.GetFileName(copies[1]) || !Equals(rows.Rows[0].Cells["Check"].Value, true))
            throw new Exception("Reorder failed or lost an exclusion checkbox");
        save.PerformClick();
        if (!Find<ProgressBar>(merge).Visible) throw new Exception("Missing merge progress");
        string combined = Path.Combine(initial, "01-spreadsheet_병합.pdf");
        await Until(() => tabs.TabPages.Count == 2, "Merge after reordering opens a new tab");
        if (!File.Exists(combined) || Directory.GetFiles(later, "*_병합.pdf").Length != 0) throw new Exception("Wrong merge location");
        if (rows.Rows.Count != 0 || save.Enabled) throw new Exception("Successful merge left files in the queue");

        var mergedView = (DocumentView)tabs.SelectedTab.Tag;
        tabs.SelectedIndex = 0;
        if (!recentToggle.Visible || recentFiles.Visible || Find<Label>(merge, "01-spreadsheet_병합.pdf · 파일 5개 · 총 8쪽") == null)
            throw new Exception("The latest successful merge was not shown as a collapsed summary");
        Capture(app, Path.Combine(data, "recent-merge-collapsed.png"));
        recentToggle.PerformClick();
        var firstDetails = recentFiles.Items.Cast<ListViewItem>().Select(row => row.Text + " " + row.SubItems[1].Text).ToArray();
        if (!recentFiles.Visible || !firstDetails.SequenceEqual(new[] { "02-presentation.pptx 2쪽", "01-spreadsheet.xlsx 1쪽",
            "03-landscape.jpg 1쪽", "04-portrait.png 1쪽", "05-three-pages.pdf 3쪽" }))
            throw new Exception("Recent input page counts do not match the merge order");
        Capture(app, Path.Combine(data, "recent-merge-expanded.png"));
        var normalSize = app.Size;
        app.Size = app.MinimumSize;
        await Task.Yield();
        if (rows.ClientSize.Height <= rows.ColumnHeadersHeight || !merge.ClientRectangle.Contains(merge.PointToClient(save.PointToScreen(Point.Empty))))
            throw new Exception(string.Format("The expanded result hides the file list or merge action in a small window. Merge view {0}, file grid {1}, header {2}, button at {3}",
                merge.ClientSize, rows.ClientSize, rows.ColumnHeadersHeight, merge.PointToClient(save.PointToScreen(Point.Empty))));
        app.Size = normalSize;
        recentToggle.PerformClick();
        if (recentFiles.Visible) throw new Exception("Closing details did not collapse the list");
        tabs.SelectedIndex = 1;
        await Until(() => Find<PictureBox>(mergedView) != null && Children(mergedView).OfType<PictureBox>().Count(p => p.Image != null) >= 3,
            "Viewer and thumbnails render");
        var checks = Children(mergedView).OfType<CheckBox>().Take(2).ToArray();
        foreach (var box in checks) box.Checked = true;
        // Accept the actual confirmation modal using the test UI thread.
        using (var accept = new System.Windows.Forms.Timer { Interval = 250 })
        {
            accept.Tick += delegate {
                IntPtr window = FindWindow("#32770", "선택 페이지 삭제");
                uint owner;
                if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out owner) == 0 || owner != Process.GetCurrentProcess().Id) return;
                PostMessage(window, 0x0111, new IntPtr(1), IntPtr.Zero);
                accept.Stop();
            };
            accept.Start();
            Find<Button>(mergedView, "선택 페이지 삭제").PerformClick();
        }
        string edited = Path.Combine(initial, "01-spreadsheet_병합_편집됨.pdf");
        await Until(() => tabs.TabPages.Count == 3, "Selected page deletion opens edited PDF");
        using (var worker = new EngineClient())
        {
            if (Count(worker, combined) != 8 || Count(worker, edited) != 6) throw new Exception("Wrong source/edited page count");
            var editedView = (DocumentView)tabs.SelectedTab.Tag;
            Invoke(app, "ShowComparison", mergedView, editedView);
            if (Find<SplitContainer>(tabs.SelectedTab) == null) throw new Exception("Comparison split missing");
            await Until(() => Children(tabs.SelectedTab).OfType<PictureBox>().Count(p => p.Image != null) == 2, "Side by side PDF render");
            Capture(app, Path.Combine(data, "comparison-preview.png"));
            var comparison = tabs.SelectedTab;
            ClickTabClose(tabs, 1);
            if (tabs.TabPages.Count != 3 || tabs.TabPages.Cast<TabPage>().Any(tab => tab.Tag == mergedView) || tabs.SelectedTab != comparison)
                throw new Exception("Closing an inactive tab changed the active tab or closed the wrong tab");
            ClickTabClose(tabs, tabs.SelectedIndex);
            if (tabs.TabPages.Count != 2 || tabs.TabPages.Contains(comparison)) throw new Exception("Close button did not close the comparison tab");
            ClickTabClose(tabs, 0);
            if (tabs.TabPages.Count != 2 || tabs.SelectedIndex != 0) throw new Exception("The merge tab no longer works as a pinned tab");
            if (Find<Button>(app, "탭 닫기") != null) throw new Exception("Old tab close toolbar button is still visible");
            Console.WriteLine("Close buttons on active and inactive tabs OK");

            string jfif = Path.Combine(later, "사진.JFIF");
            File.Copy(copies[2], jfif);
            var dropped = new DataObject(DataFormats.FileDrop, new[] { jfif, copies[4] });
            var drop = new DragEventArgs(dropped, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rows, new object[] { drop });
            await Until(() => save.Enabled, "JFIF accepted on the drag-and-drop path");
            if (rows.Rows.Count != 2 || (string)rows.Rows[0].Cells["State"].Value != "병합 대기" || merge.IsWorking)
                throw new Exception("Dropping JFIF did not just stage the files");
            string missing = copies[4] + ".test-hidden";
            File.Move(copies[4], missing);
            try
            {
                save.PerformClick();
                await Until(() => save.Enabled, "Failed merge can be retried");
                if (rows.Rows.Count != 2) throw new Exception("Failed merge cleared its inputs");
                if (Find<Label>(merge, "01-spreadsheet_병합.pdf · 파일 5개 · 총 8쪽") == null)
                    throw new Exception("A failed merge replaced the latest successful result");
            }
            finally { File.Move(missing, copies[4]); }
            save.PerformClick();
            string nextMerge = Path.Combine(later, "사진_병합.pdf");
            await Until(() => tabs.TabPages.Count == 3, "New batch opens its result");
            if (rows.Rows.Count != 0 || Count(worker, nextMerge) != 4) throw new Exception("Second batch did not reset its first input or page list");
            tabs.SelectedIndex = 0;
            if (Find<Label>(merge, "사진_병합.pdf · 파일 2개 · 총 4쪽") == null || recentFiles.Visible || recentFiles.Items.Count != 2)
                throw new Exception("A new success did not replace and collapse the previous result");
            Console.WriteLine("Recent merge expands with ordered page counts and only updates after success OK");

            var longPdf = (string)worker.Run(new { op = "merge", anchor = Path.Combine(data, "18-pages.pdf"),
                files = Enumerable.Repeat(copies[4], 6).ToArray() })["path"];
            Invoke(app, "OpenPdf", longPdf);
            await Until(() => tabs.TabPages.Count == 4, "18-page PDF opens");
            var longView = (DocumentView)tabs.SelectedTab.Tag;
            var scroll = Find<VScrollBar>(longView);
            if (scroll == null || !scroll.Visible || Children(longView).Any(c => c.AccessibleName == "다음 썸네일 목록"))
                throw new Exception("Page list does not provide continuous scrolling");
            Checkbox(longView, 1).Checked = true;
            for (int i = 0; i < 36 && Find<CheckBox>(longView, "10쪽") == null; i++)
                SendMessage(Find<PictureBox>(scroll.Parent).Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
            Checkbox(longView, 10).Checked = true;
            var tenthThumbnail = Children(scroll.Parent).OfType<PictureBox>().First(picture => picture.AccessibleName == "10쪽 보기");
            await Until(() => tenthThumbnail.Image != null, "Scrolled-to thumbnail renders");
            typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tenthThumbnail, new object[] { EventArgs.Empty });
            if (longView.Page != 9) throw new Exception("Clicking the scrolled-to thumbnail did not open its page");
            scroll.Value = scroll.Maximum - scroll.LargeChange + 1;
            Checkbox(longView, 18).Checked = true;
            scroll.Value = scroll.Minimum;
            if (!Checkbox(longView, 1).Checked || !longView.SelectedPages.OrderBy(page => page).SequenceEqual(new[] { 0, 9, 17 }))
                throw new Exception("Pages selected across scroll positions were lost");
            await Until(() => Find<PictureBox>(scroll.Parent).Image != null, "Thumbnails render again on scroll back");
            Capture(app, Path.Combine(data, "continuous-pages.png"));
            Console.WriteLine("Single scrollbar, mouse wheel through page 10, and multi-page selection across 18 pages OK");

            const string printer = "Microsoft Print to PDF";
            if (!PrinterSettings.InstalledPrinters.Cast<string>().Contains(printer))
            { Console.WriteLine("Virtual printer unavailable"); return; }
            var printed = Path.Combine(data, "spooled.pdf");
            using (var print = PrintJobs.Create(edited, 6))
            {
                var settings = print.PrinterSettings;
                settings.PrinterName = printer;
                settings.PrintToFile = true;
                settings.PrintFileName = printed;
                settings.FromPage = 2;
                settings.ToPage = 3;
                settings.PrintRange = PrintRange.SomePages;
                var counter = new PrintCounter();
                await Task.Run(() => PrintJobs.Send(print, edited, 6, worker, data, counter));
                if (!counter.Pages.SequenceEqual(new[] { 1, 2 })) throw new Exception("Print progress is incorrect");
            }
            await Until(() => File.Exists(printed) && new FileInfo(printed).Length > 0, "Windows virtual printer output");
            if (Count(worker, printed) != 2) throw new Exception("Incorrect virtual print output page count");
        }
    }

    static void VerifyButtonPaint()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using (var host = new Panel { BackColor = Theme.Surface })
        using (var button = Theme.Button("병합하기", true))
        {
            host.Controls.Add(button);
            button.Size = new Size(140, 40);
            var mouse = new MouseEventArgs(MouseButtons.Left, 1, 15, 15, 0);
            string[] states = { "initial", "hovered", "pressed", "released", "disabled" };
            foreach (string state in states)
            {
                if (state == "hovered") typeof(Control).GetMethod("OnMouseEnter", flags).Invoke(button, new object[] { EventArgs.Empty });
                if (state == "pressed") typeof(Control).GetMethod("OnMouseDown", flags).Invoke(button, new object[] { mouse });
                if (state == "released") typeof(Control).GetMethod("OnMouseUp", flags).Invoke(button, new object[] { mouse });
                if (state == "disabled") button.Enabled = false;
                using (var frame = new Bitmap(button.Width, button.Height))
                using (var graphics = Graphics.FromImage(frame))
                {
                    graphics.Clear(Color.Black);
                    using (var paint = new PaintEventArgs(graphics, button.ClientRectangle))
                        typeof(Control).GetMethod("OnPaint", flags).Invoke(button, new object[] { paint });
                    foreach (var corner in new[] { new Point(0, 0), new Point(frame.Width - 1, 0), new Point(0, frame.Height - 1),
                        new Point(frame.Width - 1, frame.Height - 1) })
                        if (frame.GetPixel(corner.X, corner.Y).ToArgb() != host.BackColor.ToArgb())
                            throw new Exception("Button did not repaint its background on a blank surface: " + state);
                }
            }
        }
        Console.WriteLine("Button paint covers the background through all pointer and disabled states OK");
    }

    static void ClickGridCell(DataGridView grid, string column, int row)
    {
        typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(grid,
            new object[] { new DataGridViewCellEventArgs(grid.Columns[column].Index, row) });
    }

    static void DragGridRow(DataGridView grid, int from, int after)
    {
        var bounds = grid.GetRowDisplayRectangle(after, true);
        var target = grid.PointToScreen(new Point(bounds.Left + 30, bounds.Bottom - 4));
        DropGridRow(grid, from, target, DragDropEffects.Move);
    }

    static void DropGridRow(DataGridView grid, int from, Point target, DragDropEffects expected)
    {
        var dragging = grid.GetType().GetField("dragged", BindingFlags.Instance | BindingFlags.NonPublic);
        var args = new DragEventArgs(new DataObject("JinPDF.MergeRow", "row"), 0, target.X, target.Y, DragDropEffects.Move, DragDropEffects.None);
        dragging.SetValue(grid, grid.Rows[from]);
        try
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Control).GetMethod("OnDragOver", flags).Invoke(grid, new object[] { args });
            if (args.Effect != expected) throw new Exception("The file list used the wrong action for an internal row drag");
            typeof(Control).GetMethod("OnDragDrop", flags).Invoke(grid, new object[] { args });
        }
        finally { dragging.SetValue(grid, null); }
    }

    static CheckBox Checkbox(DocumentView view, int page)
    {
        var box = Find<CheckBox>(view, page + "쪽");
        if (box == null) throw new Exception("Page " + page + " was not available at its scroll position");
        return box;
    }

    static void ClickTabClose(TabControl tabs, int index)
    {
        var tab = tabs.GetTabRect(index);
        var point = new IntPtr((tab.Right - 15) | ((tab.Top + tab.Height / 2) << 16));
        SendMessage(tabs.Handle, 0x0201, new IntPtr(1), point);
        SendMessage(tabs.Handle, 0x0202, IntPtr.Zero, point);
    }

    static void Capture(Form app, string path)
    {
        using (var image = new Bitmap(app.Width, app.Height))
        {
            app.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(path);
        }
    }

    sealed class PrintCounter : IProgress<int>
    {
        public readonly List<int> Pages = new List<int>();
        public void Report(int page) { Pages.Add(page); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Specify a writable, empty test directory.");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string data = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("JINPDF_TEMP", data);
        int result = 1;
        var app = new MainWindow(new string[0]);
        var watch = Stopwatch.StartNew();
        app.Shown += async delegate {
            Console.WriteLine("Window shown in " + watch.ElapsedMilliseconds + "ms");
            try { await Run(app, data); result = 0; }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { Application.ExitThread(); }
        };
        Application.Run(app);
        return result;
    }
}
