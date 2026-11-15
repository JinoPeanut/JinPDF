using System;
using System.Drawing;
using System.Windows.Forms;

sealed class PdfTabs : TabControl
{
    const int LeftDown = 0x0201;
    const int LeftUp = 0x0202;
    const int LeftDoubleClick = 0x0203;
    TabPage pressedClose;

    public event Action<TabPage> CloseRequested;

    public static Rectangle CloseBounds(Rectangle tab)
    {
        return new Rectangle(tab.Right - 27, tab.Top, 24, tab.Height);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == LeftDown || message.Msg == LeftDoubleClick)
        {
            pressedClose = CloseTarget(message.LParam);
            if (pressedClose != null)
            {
                Capture = true;
                return;
            }
        }
        else if (message.Msg == LeftUp && pressedClose != null)
        {
            var page = pressedClose;
            pressedClose = null;
            Capture = false;
            if (page == CloseTarget(message.LParam) && CloseRequested != null) CloseRequested(page);
            return;
        }
        base.WndProc(ref message);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) pressedClose = null;
        base.OnMouseCaptureChanged(e);
    }

    TabPage CloseTarget(IntPtr position)
    {
        long packed = position.ToInt64();
        var point = new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
        if (!ClientRectangle.Contains(point)) return null;
        // The first tab is the permanent merge workspace.
        for (int i = 1; i < TabCount; i++)
            if (CloseBounds(GetTabRect(i)).Contains(point)) return TabPages[i];
        return null;
    }
}
