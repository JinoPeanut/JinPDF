using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

sealed class MergeFileGrid : DataGridView
{
    public const string Grip = "Grip";
    public const string Check = "Check";
    public const string FileNameColumn = "File";
    public const string State = "State";
    public const string Exclude = "Exclude";
    const string RowDragFormat = "JinPDF.MergeRow";

    DataGridViewRow dragCandidate;
    DataGridViewRow dragged;
    Rectangle dragThreshold;
    int dropIndex = -1;
    int scrollTick;

    public event Action<DataGridViewRow[]> ExcludeRequested;
    public event Action<string[]> FilesDropped;
    public event Action ChecksChanged;

    public MergeFileGrid()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        AllowDrop = true;
        Margin = Padding.Empty;
        AccessibleName = "병합할 파일 목록";
        BackgroundColor = Color.White;
        BorderStyle = BorderStyle.None;
        GridColor = Theme.Line;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        EnableHeadersVisualStyles = false;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        ColumnHeadersHeight = 37;
        ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
        ColumnHeadersDefaultCellStyle.ForeColor = Theme.Ink;
        ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Surface;
        ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Ink;
        DefaultCellStyle.BackColor = Color.White;
        DefaultCellStyle.ForeColor = Theme.Ink;
        DefaultCellStyle.SelectionBackColor = Theme.Surface;
        DefaultCellStyle.SelectionForeColor = Theme.Ink;
        RowTemplate.Height = 42;
        RowHeadersVisible = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeColumns = false;
        AllowUserToResizeRows = false;
        SelectionMode = DataGridViewSelectionMode.CellSelect;
        MultiSelect = false;
        EditMode = DataGridViewEditMode.EditOnEnter;

        Columns.Add(TextColumn(Grip, "", 33));
        Columns.Add(new DataGridViewCheckBoxColumn { Name = Check, HeaderText = "", Width = 39,
            SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = "목록에서 제외할 파일을 선택합니다." });
        var file = TextColumn(FileNameColumn, "파일 이름", 0);
        file.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        file.FillWeight = 66;
        file.MinimumWidth = 155;
        file.HeaderCell.Style.Padding = new Padding(6, 0, 0, 0);
        Columns.Add(file);
        var state = TextColumn(State, "상태", 0);
        state.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        state.FillWeight = 34;
        state.MinimumWidth = 120;
        Columns.Add(state);
        var exclude = TextColumn(Exclude, "제외", 64);
        exclude.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        exclude.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        Columns.Add(exclude);
    }

    static DataGridViewTextBoxColumn TextColumn(string name, string caption, int width)
    {
        var column = new DataGridViewTextBoxColumn { Name = name, HeaderText = caption, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable };
        if (width > 0) column.Width = width;
        return column;
    }

    public DataGridViewRow[] CheckedRows()
    {
        return Rows.Cast<DataGridViewRow>().Where(row => Equals(row.Cells[Check].Value, true)).ToArray();
    }

    public DataGridViewRow AddFile(string path, object entry)
    {
        var row = new DataGridViewRow { Tag = entry, Height = RowTemplate.Height };
        row.CreateCells(this, null, false, Path.GetFileName(path), "병합 대기", "×");
        row.Cells[Columns[FileNameColumn].Index].ToolTipText = path;
        row.Cells[Columns[Grip].Index].ToolTipText = "드래그 또는 Alt+↑/↓로 순서 변경";
        row.Cells[Columns[Exclude].Index].ToolTipText = "이 파일을 병합 목록에서 제외";
        Rows.Add(row);
        return row;
    }

    protected override void OnCurrentCellDirtyStateChanged(EventArgs e)
    {
        if (IsCurrentCellDirty && CurrentCell is DataGridViewCheckBoxCell) CommitEdit(DataGridViewDataErrorContexts.Commit);
        base.OnCurrentCellDirtyStateChanged(e);
    }

    protected override void OnCellValueChanged(DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && e.RowIndex < Rows.Count && e.ColumnIndex == Columns[Check].Index)
        {
            var row = Rows[e.RowIndex];
            bool selected = Equals(row.Cells[Check].Value, true);
            row.DefaultCellStyle.BackColor = selected ? Theme.Tint : Color.White;
            row.DefaultCellStyle.SelectionBackColor = selected ? Theme.Tint : Theme.Surface;
            if (ChecksChanged != null) ChecksChanged();
        }
        base.OnCellValueChanged(e);
    }

    protected override void OnCellClick(DataGridViewCellEventArgs e)
    {
        base.OnCellClick(e);
        if (!ReadOnly && e.RowIndex >= 0 && e.ColumnIndex == Columns[Exclude].Index && ExcludeRequested != null)
            ExcludeRequested(new[] { Rows[e.RowIndex] });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!ReadOnly && CurrentRow != null)
        {
            if (e.KeyData == (Keys.Alt | Keys.Up) || e.KeyData == (Keys.Alt | Keys.Down))
            {
                MoveRow(CurrentRow, CurrentRow.Index + (e.KeyCode == Keys.Up ? -1 : 1));
                e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyData == Keys.Delete || (e.KeyData == Keys.Enter && CurrentCell.OwningColumn.Name == Exclude))
            {
                var checkedRows = e.KeyData == Keys.Delete ? CheckedRows() : new DataGridViewRow[0];
                if (ExcludeRequested != null) ExcludeRequested(checkedRows.Length > 0 ? checkedRows : new[] { CurrentRow });
                e.SuppressKeyPress = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!ReadOnly && (keyData == Keys.Delete || keyData == (Keys.Alt | Keys.Up) || keyData == (Keys.Alt | Keys.Down)))
        {
            var key = new KeyEventArgs(keyData);
            OnKeyDown(key);
            if (key.Handled) return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        dragCandidate = null;
        var hit = HitTest(e.X, e.Y);
        if (!ReadOnly && e.Button == MouseButtons.Left && hit.RowIndex >= 0 && hit.ColumnIndex == Columns[Grip].Index)
        {
            dragCandidate = Rows[hit.RowIndex];
            var size = SystemInformation.DragSize;
            dragThreshold = new Rectangle(e.X - size.Width / 2, e.Y - size.Height / 2, size.Width, size.Height);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = HitTest(e.X, e.Y);
        Cursor = !ReadOnly && hit.RowIndex >= 0 && hit.ColumnIndex == Columns[Grip].Index ? Cursors.SizeNS :
            !ReadOnly && hit.RowIndex >= 0 && hit.ColumnIndex == Columns[Exclude].Index ? Cursors.Hand : Cursors.Default;
        if (dragCandidate != null && e.Button == MouseButtons.Left && !dragThreshold.Contains(e.Location))
        {
            dragged = dragCandidate;
            dragCandidate = null;
            try { DoDragDrop(new DataObject(RowDragFormat, "row"), DragDropEffects.Move); }
            finally { dragged = null; SetDropIndex(-1); }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        dragCandidate = null;
        base.OnMouseUp(e);
    }

    protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); OnDragOver(e); }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effect = DragDropEffects.None;
        if (ReadOnly) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effect = DragDropEffects.Copy;
            return;
        }
        if (dragged == null || !e.Data.GetDataPresent(RowDragFormat)) return;
        var point = PointToClient(new Point(e.X, e.Y));
        if (InsertionIndex(point) < 0) { SetDropIndex(-1); return; }
        e.Effect = DragDropEffects.Move;
        if (unchecked(Environment.TickCount - scrollTick) > 90 && FirstDisplayedScrollingRowIndex >= 0)
        {
            int first = FirstDisplayedScrollingRowIndex;
            if (point.Y < ColumnHeadersHeight + 11 && first > 0) FirstDisplayedScrollingRowIndex = first - 1;
            else if (point.Y > ClientSize.Height - 18 && first + DisplayedRowCount(false) < Rows.Count)
                FirstDisplayedScrollingRowIndex = first + 1;
            scrollTick = Environment.TickCount;
        }
        SetDropIndex(InsertionIndex(point));
    }

    protected override void OnDragLeave(EventArgs e) { SetDropIndex(-1); base.OnDragLeave(e); }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        SetDropIndex(-1);
        if (ReadOnly) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (FilesDropped != null) FilesDropped((string[])e.Data.GetData(DataFormats.FileDrop));
            return;
        }
        if (dragged == null || !e.Data.GetDataPresent(RowDragFormat)) return;
        int position = InsertionIndex(PointToClient(new Point(e.X, e.Y)));
        if (position > dragged.Index) position--;
        MoveRow(dragged, position);
    }

    int InsertionIndex(Point point)
    {
        if (!ClientRectangle.Contains(point)) return -1;
        var hit = HitTest(point.X, point.Y);
        if (hit.Type == DataGridViewHitTestType.VerticalScrollBar || hit.Type == DataGridViewHitTestType.HorizontalScrollBar) return -1;
        if (point.Y < ColumnHeadersHeight) return Math.Max(0, FirstDisplayedScrollingRowIndex);
        if (hit.RowIndex < 0) return Rows.Count;
        var row = GetRowDisplayRectangle(hit.RowIndex, false);
        return hit.RowIndex + (point.Y >= row.Top + row.Height / 2 ? 1 : 0);
    }

    void MoveRow(DataGridViewRow row, int position)
    {
        if (position < 0 || position >= Rows.Count || row.Index < 0 || row.Index == position) return;
        Rows.Remove(row);
        Rows.Insert(position, row);
        CurrentCell = row.Cells[FileNameColumn];
    }

    void SetDropIndex(int index)
    {
        if (index == dropIndex) return;
        dropIndex = index;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (dropIndex < 0 || Rows.Count == 0) return;
        var bounds = GetRowDisplayRectangle(Math.Min(dropIndex, Rows.Count - 1), true);
        if (bounds.Height == 0) return;
        int y = dropIndex == Rows.Count ? bounds.Bottom - 2 : bounds.Top;
        using (var pen = new Pen(Theme.Accent, 2)) e.Graphics.DrawLine(pen, 0, y, ClientSize.Width, y);
    }

    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        base.OnCellPainting(e);
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        string name = Columns[e.ColumnIndex].Name;
        if (name != FileNameColumn && name != Grip && name != Exclude) return;
        e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
        var cell = e.CellBounds;
        if (name == Grip)
        {
            int x = cell.Left + cell.Width / 2 - 4;
            int y = cell.Top + cell.Height / 2 - 6;
            using (var brush = new SolidBrush(Theme.Muted))
                for (int dot = 0; dot < 6; dot++) e.Graphics.FillEllipse(brush, x + dot % 2 * 6, y + dot / 2 * 5, 3, 3);
        }
        else if (name == Exclude)
        {
            TextRenderer.DrawText(e.Graphics, "×", Font, cell, ReadOnly ? Theme.Muted : Theme.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            string filename = Convert.ToString(e.FormattedValue);
            Theme.DrawFileIcon(e.Graphics, new Rectangle(cell.Left + 7, cell.Top + (cell.Height - 23) / 2, 20, 23), Path.GetExtension(filename));
            var label = new Rectangle(cell.Left + 37, cell.Top, Math.Max(0, cell.Width - 42), cell.Height);
            TextRenderer.DrawText(e.Graphics, filename, Font, label, Theme.Ink, TextFormatFlags.NoPrefix |
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        e.Handled = true;
    }
}
