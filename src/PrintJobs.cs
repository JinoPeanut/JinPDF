using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;

static class PrintJobs
{
    public static PrintDocument Create(string path, int count)
    {
        var document = new PrintDocument { DocumentName = Path.GetFileName(path), PrintController = new StandardPrintController() };
        document.PrinterSettings.MinimumPage = 1;
        document.PrinterSettings.MaximumPage = count;
        document.PrinterSettings.FromPage = 1;
        document.PrinterSettings.ToPage = count;
        return document;
    }

    public static void Send(PrintDocument document, string path, int count, EngineClient engine, string temporary,
        IProgress<int> progress, int? marginMillimeters = null)
    {
        var settings = document.PrinterSettings;
        int first = settings.PrintRange == PrintRange.SomePages ? settings.FromPage : 1;
        int last = settings.PrintRange == PrintRange.SomePages ? settings.ToPage : count;
        if (first < 1 || last > count || last < first) throw new ArgumentException("인쇄할 페이지 범위를 확인하세요.");
        int index = first - 1;
        PrintPageEventHandler page = delegate(object sender, PrintPageEventArgs e) {
            using (var image = engine.Render(path, index, 2350, temporary))
            {
                RectangleF target;
                if (marginMillimeters.HasValue)
                {
                    // With OriginAtMargins off, graphics coordinates start at the printable area.
                    var printable = e.PageSettings.PrintableArea;
                    float margin = marginMillimeters.Value * 100 / 25.4f;
                    var inset = new RectangleF(margin - printable.X, margin - printable.Y,
                        e.PageBounds.Width - 2 * margin, e.PageBounds.Height - 2 * margin);
                    target = RectangleF.Intersect(new RectangleF(PointF.Empty, printable.Size), inset);
                    if (target.Width <= 0 || target.Height <= 0)
                        throw new ArgumentException("이 용지에는 설정한 여백을 남길 수 없습니다.");
                }
                else
                {
                    target = e.MarginBounds;
                    target.Offset(-(int)e.PageSettings.HardMarginX, -(int)e.PageSettings.HardMarginY);
                }
                Theme.Fit(e.Graphics, image, target);
            }
            index++;
            progress.Report(index - first + 1);
            e.HasMorePages = index < last;
        };
        document.PrintPage += page;
        try { document.Print(); }
        finally { document.PrintPage -= page; }
    }
}
