using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class DocumentView : UserControl
{
    const int FitZoom = 3;
    const int WheelNotch = 120;
    const int StandardResolution = 1200;
    static readonly double[] ZoomFactors = { 0.5, 0.67, 0.8, 1.0, 1.25, 1.6, 2.0, 2.5, 3.0, 4.0 };
    readonly EngineClient preview;
    readonly string temporary;
    readonly Action<string> status;
    readonly Label counter = Theme.Text("", 10, true);
    readonly PictureBox canvas = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Surface };
    readonly Panel viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface, Padding = new Padding(16) };
    readonly ComboBox zoom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 76, Margin = new Padding(0, 2, 8, 0) };
    readonly PageThumbnails pageList;
    readonly Button previous = Theme.Button("‹");
    readonly Button next = Theme.Button("›");
    readonly Dictionary<int, int> rotations = new Dictionary<int, int>();
    readonly ToolTip tips = new ToolTip();
    readonly CheckBox sharp = new CheckBox { Text = "선명하게", Checked = true, AutoSize = true,
        Margin = new Padding(3, 7, 10, 0) };
    readonly Timer detailDelay = new Timer { Interval = 200 };
    int renderedPage = -1;
    bool initialized;
    int generation;
    int wheelRemainder;
    int displayedResolution;
    bool refreshingDetail;
    public string FilePath { get; private set; }
    public int Count { get; private set; }
    public int Page { get; private set; }
    public IEnumerable<int> SelectedPages { get { return pageList == null ? new int[0] : pageList.SelectedPages; } }
    public event Action DeleteRequested;

    public DocumentView(string path, int count, EngineClient preview, EngineClient thumbnails,
        string temporary, Action<string> status, bool showThumbnails, int initialPage)
    {
        FilePath = path;
        Count = count;
        Page = initialPage;
        this.preview = preview;
        this.temporary = temporary;
        this.status = status;
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = showThumbnails ? 2 : 1,
            RowCount = 2, Padding = new Padding(0), Margin = Padding.Empty, BackColor = Color.White };
        if (showThumbnails) layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var toolbarHeight = new RowStyle(SizeType.Absolute, 47);
        layout.RowStyles.Add(toolbarHeight);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tools = Theme.Row();
        tools.Padding = new Padding(12, 7, 0, 5);
        tools.WrapContents = true;
        tools.Layout += delegate {
            int bottom = tools.Controls.Cast<Control>().Select(control => control.Bottom + control.Margin.Bottom).DefaultIfEmpty(0).Max();
            int height = Math.Max(47, bottom + tools.Padding.Bottom);
            if (toolbarHeight.Height != height) toolbarHeight.Height = height;
        };
        counter.Margin = new Padding(5, 7, 13, 0);
        previous.AccessibleName = "이전 페이지";
        next.AccessibleName = "다음 페이지";
        previous.Click += delegate { Navigate(Page - 1); };
        next.Click += delegate { Navigate(Page + 1); };
        var jump = Theme.Button("이동");
        jump.Click += delegate { Jump(); };
        zoom.Items.AddRange(new object[] { "50%", "67%", "80%", "맞춤", "125%", "160%", "200%", "250%", "300%", "400%" });
        zoom.SelectedIndex = FitZoom;
        viewport.Controls.Add(canvas);
        Action applyZoom = delegate {
            if (zoom.SelectedIndex == FitZoom) { canvas.Dock = DockStyle.Fill; canvas.SizeMode = PictureBoxSizeMode.Zoom; return; }
            if (canvas.Image == null) return;
            double factor = ZoomFactors[zoom.SelectedIndex];
            int width = Math.Max(100, viewport.ClientSize.Width - 44);
            int height = Math.Max(100, viewport.ClientSize.Height - 44);
            double fit = Math.Min((double)width / canvas.Image.Width, (double)height / canvas.Image.Height) * factor;
            canvas.Dock = DockStyle.None;
            canvas.Location = new Point(16, 16);
            canvas.Size = new Size((int)(canvas.Image.Width * fit), (int)(canvas.Image.Height * fit));
        };
        zoom.SelectedIndexChanged += delegate { applyZoom(); ScheduleDetail(); };
        sharp.CheckedChanged += delegate { ScheduleDetail(); };
        detailDelay.Tick += delegate { detailDelay.Stop(); RefreshDetail(); };
        viewport.Resize += delegate { applyZoom(); };
        canvas.SizeChanged += delegate { if (canvas.Image != null && zoom.SelectedIndex == FitZoom) canvas.Invalidate(); };
        ImageChanged = applyZoom;
        tips.SetToolTip(sharp, "200% 이상에서 고해상도로 그려 글자와 표의 윤곽을 또렷하게 표시합니다.");
        tools.Controls.AddRange(new Control[] { previous, next, counter, jump, zoom, sharp });
        var rotateLeft = Theme.Button("↶ 왼쪽 회전");
        var rotateRight = Theme.Button("↷ 오른쪽 회전");
        rotateLeft.Click += delegate { RotatePage(-1); };
        rotateRight.Click += delegate { RotatePage(1); };
        tips.SetToolTip(rotateLeft, "현재 페이지를 왼쪽으로 90° 회전 (Ctrl + Shift + ←)");
        tips.SetToolTip(rotateRight, "현재 페이지를 오른쪽으로 90° 회전 (Ctrl + Shift + →)");
        tools.Controls.AddRange(new Control[] { rotateLeft, rotateRight });
        if (showThumbnails)
        {
            var remove = Theme.Button("선택 페이지 삭제");
            remove.Click += delegate { if (DeleteRequested != null) DeleteRequested(); };
            remove.AutoSize = false;
            remove.MinimumSize = Size.Empty;
            remove.Dock = DockStyle.Fill;
            remove.Margin = new Padding(8, 5, 8, 5);
            var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
            sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            var heading = Theme.Text(string.Format("페이지  {0:N0}쪽", count), 10, true);
            heading.Margin = new Padding(10, 10, 0, 0);
            var selectionHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
                RowCount = 3, Margin = Padding.Empty };
            selectionHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selectionHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            selectionHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            selectionHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selectionHeader.Controls.Add(heading, 0, 0);
            var selectAll = Theme.Button("전체선택");
            var clearSelection = Theme.Button("선택 해제");
            foreach (var button in new[] { selectAll, clearSelection })
            {
                button.AutoSize = false;
                button.MinimumSize = Size.Empty;
                button.Dock = DockStyle.Fill;
                button.Font = new Font("Malgun Gothic", 9);
                button.Padding = new Padding(2, 0, 2, 0);
                button.Margin = new Padding(0, 0, 5, 5);
            }
            var selectionTools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2,
                RowCount = 1, Margin = Padding.Empty };
            selectionTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            selectionTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            selectionTools.Padding = new Padding(8, 0, 0, 0);
            selectionTools.Controls.Add(selectAll, 0, 0);
            selectionTools.Controls.Add(clearSelection, 1, 0);
            selectionHeader.Controls.Add(selectionTools, 0, 1);
            var selectedCount = Theme.Text("");
            selectedCount.Margin = new Padding(10, 3, 0, 0);
            selectionHeader.Controls.Add(selectedCount, 0, 2);
            sidebar.Controls.Add(selectionHeader, 0, 0);
            pageList = new PageThumbnails(count, index => thumbnails.Render(path, index, 180, temporary), Navigate);
            Action updateSelection = delegate {
                int selected = pageList.SelectedPages.Count();
                selectedCount.Text = string.Format("{0:N0} / {1:N0}쪽 선택", selected, Count);
                selectAll.Enabled = selected < Count;
                clearSelection.Enabled = selected > 0;
                remove.Enabled = selected > 0;
            };
            pageList.SelectionChanged += updateSelection;
            selectAll.Click += delegate { pageList.SelectPages(true); };
            clearSelection.Click += delegate { pageList.SelectPages(false); };
            updateSelection();
            sidebar.Controls.Add(pageList, 0, 1);
            sidebar.Controls.Add(remove, 0, 2);
            layout.Controls.Add(sidebar, 0, 1);
        }
        layout.Controls.Add(tools, 0, 0);
        if (showThumbnails) layout.SetColumnSpan(tools, 2);
        layout.Controls.Add(viewport, showThumbnails ? 1 : 0, 1);
        Controls.Add(layout);
    }

    Action ImageChanged;

    int DetailResolution
    {
        get
        {
            double zoomFactor = ZoomFactors[zoom.SelectedIndex];
            return sharp.Checked && zoomFactor >= 2 ? (int)(StandardResolution * zoomFactor) : StandardResolution;
        }
    }

    void ScheduleDetail()
    {
        detailDelay.Stop();
        if (!refreshingDetail && renderedPage == Page && NeedsDetail) detailDelay.Start();
    }

    bool NeedsDetail { get { return sharp.Checked ? displayedResolution < DetailResolution : displayedResolution > StandardResolution; } }

    async void RefreshDetail()
    {
        if (renderedPage != Page || !NeedsDetail) return;
        int index = Page;
        int version = generation;
        int pixels = DetailResolution;
        refreshingDetail = true;
        bool settingsChanged = false;
        try
        {
            var image = await Task.Run(() => preview.Render(FilePath, index, pixels, temporary, pixels > StandardResolution));
            if (IsDisposed) { image.Dispose(); return; }
            settingsChanged = version != generation || pixels != DetailResolution;
            if (settingsChanged) { image.Dispose(); return; }
            DisplayImage(image, index, pixels);
        }
        catch (Exception)
        {
            if (!IsDisposed)
            {
                settingsChanged = version != generation || pixels != DetailResolution;
                if (!settingsChanged) status("선명한 보기로 전환하지 못했습니다. 현재 화면을 유지합니다.");
            }
        }
        finally
        {
            refreshingDetail = false;
            if (!IsDisposed && settingsChanged) ScheduleDetail();
        }
    }

    void DisplayImage(Bitmap image, int index, int pixels)
    {
        int turns;
        rotations.TryGetValue(index, out turns);
        if (turns != 0) image.RotateFlip((RotateFlipType)turns);
        var old = canvas.Image;
        canvas.Image = image;
        renderedPage = index;
        displayedResolution = pixels;
        if (old != null) old.Dispose();
    }

    public void RotatePage(int steps)
    {
        int turns;
        rotations.TryGetValue(Page, out turns);
        turns = (turns + steps % 4 + 4) % 4;
        rotations[Page] = turns;
        if (renderedPage == Page && canvas.Image != null)
        {
            canvas.Image.RotateFlip((RotateFlipType)((steps % 4 + 4) % 4));
            ImageChanged();
            canvas.Invalidate();
        }
        status(string.Format("{0:N0}쪽 · 보기 회전 {1}°", Page + 1, turns * 90));
    }

    public void ChangeZoom(int steps)
    {
        zoom.SelectedIndex = Math.Max(0, Math.Min(zoom.Items.Count - 1, zoom.SelectedIndex + steps));
    }

    public bool ZoomFromWheel(Point screenPoint, int delta)
    {
        if (!viewport.ClientRectangle.Contains(viewport.PointToClient(screenPoint))) return false;
        wheelRemainder += delta;
        int steps = wheelRemainder / WheelNotch;
        wheelRemainder %= WheelNotch;
        if (steps != 0) ChangeZoom(steps);
        return true;
    }

    public void ActivateView()
    {
        if (initialized) return;
        initialized = true;
        Navigate(Page);
    }

    void Jump()
    {
        using (var dialog = new Form { Text = "페이지 이동", ClientSize = new Size(225, 110),
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false })
        {
            var input = new NumericUpDown { Minimum = 1, Maximum = Count, Value = Page + 1, Location = new Point(18, 18) };
            var go = Theme.Button("이동", true);
            go.Location = new Point(18, 57);
            go.DialogResult = DialogResult.OK;
            dialog.Controls.AddRange(new Control[] { input, go });
            dialog.AcceptButton = go;
            if (dialog.ShowDialog(this) == DialogResult.OK) Navigate((int)input.Value - 1);
        }
    }

    public async void Navigate(int index)
    {
        if (index < 0 || index >= Count) return;
        Page = index;
        counter.Text = string.Format("{0:N0} / {1:N0}쪽", index + 1, Count);
        previous.Enabled = index > 0;
        next.Enabled = index + 1 < Count;
        int version = ++generation;
        detailDelay.Stop();
        renderedPage = -1;
        if (pageList != null) pageList.ShowPage(index);
        try
        {
            var image = await Task.Run(() => preview.Render(FilePath, index, StandardResolution, temporary));
            if (IsDisposed || version != generation) { image.Dispose(); return; }
            DisplayImage(image, index, StandardResolution);
            ImageChanged();
            ScheduleDetail();
        }
        catch (Exception error) { if (!IsDisposed && version == generation) status("페이지를 열 수 없습니다. " + error.Message); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ++generation;
            detailDelay.Dispose();
            tips.Dispose();
            if (canvas.Image != null) { canvas.Image.Dispose(); canvas.Image = null; }
        }
        base.Dispose(disposing);
    }
}
