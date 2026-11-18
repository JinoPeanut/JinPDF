using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class DocumentView : UserControl
{
    const int FitZoom = 3;
    const int WheelNotch = 120;
    static readonly double[] ZoomFactors = { 0.5, 0.67, 0.8, 1.0, 1.25, 1.6, 2.0 };
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
    bool initialized;
    int generation;
    int wheelRemainder;
    int resolution = 1200;
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tools = Theme.Row();
        tools.Padding = new Padding(12, 7, 0, 0);
        counter.Margin = new Padding(5, 7, 13, 0);
        previous.AccessibleName = "이전 페이지";
        next.AccessibleName = "다음 페이지";
        previous.Click += delegate { Navigate(Page - 1); };
        next.Click += delegate { Navigate(Page + 1); };
        var jump = Theme.Button("이동");
        jump.Click += delegate { Jump(); };
        zoom.Items.AddRange(new object[] { "50%", "67%", "80%", "맞춤", "125%", "160%", "200%" });
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
        zoom.SelectedIndexChanged += delegate { applyZoom(); };
        viewport.Resize += delegate { applyZoom(); };
        canvas.SizeChanged += delegate { if (canvas.Image != null && zoom.SelectedIndex == FitZoom) canvas.Invalidate(); };
        ImageChanged = applyZoom;
        tools.Controls.AddRange(new Control[] { previous, next, counter, jump, zoom });
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
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            var heading = Theme.Text(string.Format("페이지  {0:N0}쪽", count), 10, true);
            heading.Margin = new Padding(10, 10, 0, 0);
            sidebar.Controls.Add(heading, 0, 0);
            pageList = new PageThumbnails(count, index => thumbnails.Render(path, index, 180, temporary), Navigate);
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
        if (pageList != null) pageList.ShowPage(index);
        try
        {
            var image = await Task.Run(() => preview.Render(FilePath, index, resolution, temporary));
            if (IsDisposed || version != generation) { image.Dispose(); return; }
            var old = canvas.Image;
            canvas.Image = image;
            if (old != null) old.Dispose();
            ImageChanged();
        }
        catch (Exception error) { if (!IsDisposed && version == generation) status("페이지를 열 수 없습니다. " + error.Message); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ++generation;
            if (canvas.Image != null) { canvas.Image.Dispose(); canvas.Image = null; }
        }
        base.Dispose(disposing);
    }
}
