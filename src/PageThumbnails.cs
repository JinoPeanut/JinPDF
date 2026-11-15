using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class PageThumbnails : UserControl
{
    const int TileHeight = 138;
    readonly Panel viewport = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
    readonly VScrollBar scroll = new VScrollBar { Dock = DockStyle.Right, SmallChange = TileHeight / 3 };
    readonly Dictionary<int, Tile> tiles = new Dictionary<int, Tile>();
    readonly HashSet<int> selection = new HashSet<int>();
    readonly Func<int, Bitmap> render;
    readonly Action<int> open;
    readonly int count;
    bool rendering;
    int currentPage;

    sealed class Tile
    {
        public Panel Card;
        public PictureBox Picture;
        public bool Requested;

        public void Dispose()
        {
            if (Picture.Image != null)
            {
                Picture.Image.Dispose();
                Picture.Image = null;
            }
            Card.Dispose();
        }
    }

    public IEnumerable<int> SelectedPages { get { return selection; } }

    public PageThumbnails(int count, Func<int, Bitmap> render, Action<int> open)
    {
        this.count = count;
        this.render = render;
        this.open = open;
        AccessibleName = "페이지 썸네일";
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = Color.White;
        scroll.AccessibleName = "페이지 목록 스크롤";
        scroll.Maximum = count * TileHeight - 1;
        viewport.Resize += delegate { Arrange(); };
        viewport.MouseWheel += ScrollWheel;
        scroll.ValueChanged += delegate { Arrange(); };
        Controls.Add(viewport);
        Controls.Add(scroll);
    }

    public void ShowPage(int page)
    {
        currentPage = page;
        int start = page * TileHeight;
        if (start < scroll.Value) scroll.Value = start;
        else if (start + TileHeight > scroll.Value + viewport.ClientSize.Height)
            scroll.Value = Math.Max(0, Math.Min(MaxOffset, start + TileHeight - viewport.ClientSize.Height));
        Arrange();
    }

    int MaxOffset { get { return Math.Max(0, count * TileHeight - viewport.ClientSize.Height); } }

    void ScrollWheel(object sender, MouseEventArgs e)
    {
        if (MaxOffset == 0) return;
        int lines = SystemInformation.MouseWheelScrollLines;
        int distance = lines < 0 ? scroll.LargeChange : lines * scroll.SmallChange;
        scroll.Value = Math.Max(0, Math.Min(MaxOffset, scroll.Value - e.Delta * distance / 120));
        var handled = e as HandledMouseEventArgs;
        if (handled != null) handled.Handled = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollWheel(this, e);
        base.OnMouseWheel(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) Arrange();
    }

    void Arrange()
    {
        if (IsDisposed || Disposing || viewport.ClientSize.Height <= 0) return;
        scroll.LargeChange = viewport.ClientSize.Height;
        scroll.Visible = MaxOffset > 0;
        if (scroll.Value > MaxOffset) scroll.Value = MaxOffset;
        int offset = scroll.Value;
        int first = offset / TileHeight;
        int end = Math.Min(count, (offset + viewport.ClientSize.Height - 1) / TileHeight + 1);
        foreach (int page in tiles.Keys.Where(page => page < first || page >= end).ToArray())
        {
            tiles[page].Dispose();
            tiles.Remove(page);
        }

        int width = Math.Max(44, viewport.ClientSize.Width - 16);
        for (int page = first; page < end; page++)
        {
            Tile tile;
            if (!tiles.TryGetValue(page, out tile))
            {
                tile = CreateTile(page);
                tiles.Add(page, tile);
                viewport.Controls.Add(tile.Card);
            }
            tile.Card.Bounds = new Rectangle(8, page * TileHeight - offset + 5, width, TileHeight - 10);
            tile.Picture.Width = width - 12;
            tile.Card.BackColor = page == currentPage ? Theme.Line : Theme.Surface;
        }
        if (Visible && !rendering) RenderVisible();
    }

    Tile CreateTile(int page)
    {
        var card = new Panel { TabIndex = page };
        var pick = new CheckBox { Text = (page + 1) + "쪽", Checked = selection.Contains(page), AutoSize = true,
            Location = new Point(8, 101) };
        var picture = new PictureBox { Location = new Point(6, 4), Height = 94, SizeMode = PictureBoxSizeMode.Zoom,
            Cursor = Cursors.Hand, AccessibleName = (page + 1) + "쪽 보기" };
        pick.CheckedChanged += delegate { if (pick.Checked) selection.Add(page); else selection.Remove(page); };
        picture.Click += delegate { open(page); };
        foreach (Control target in new Control[] { card, pick, picture }) target.MouseWheel += ScrollWheel;
        card.Controls.AddRange(new Control[] { pick, picture });
        return new Tile { Card = card, Picture = picture };
    }

    async void RenderVisible()
    {
        rendering = true;
        try
        {
            while (Visible && !IsDisposed && !Disposing)
            {
                var next = tiles.Where(item => !item.Value.Requested).OrderBy(item => item.Key).FirstOrDefault();
                if (next.Value == null) return;
                next.Value.Requested = true;
                int page = next.Key;
                Bitmap image;
                try { image = await Task.Run(() => render(page)); }
                catch (Exception) { continue; }
                Tile active;
                if (!IsDisposed && !Disposing && tiles.TryGetValue(page, out active) && active == next.Value)
                    active.Picture.Image = image;
                else image.Dispose();
            }
        }
        finally { rendering = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var tile in tiles.Values) tile.Dispose();
            tiles.Clear();
        }
        base.Dispose(disposing);
    }
}
