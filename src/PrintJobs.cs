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

    public static void Send(PrintDocument document, string path, int count, EngineClient engine, string temporary, IProgress<int> progress)
    {
        var settings = document.PrinterSettings;
        int first = settings.PrintRange == PrintRange.SomePages ? settings.FromPage : 1;
        int last = settings.PrintRange == PrintRange.SomePages ? settings.ToPage : count;
        if (first < 1 || last > count || last < first) throw new ArgumentException("인쇄할 페이지 범위를 확인하세요.");
        int index = first - 1;
        PrintPageEventHandler page = delegate(object sender, PrintPageEventArgs e) {
            using (var image = engine.Render(path, index, 2350, temporary))
            {
                var target = e.MarginBounds;
                target.Offset(-(int)e.PageSettings.HardMarginX, -(int)e.PageSettings.HardMarginY);
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
