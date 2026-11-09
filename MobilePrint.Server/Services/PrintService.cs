using System.Drawing.Printing;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using MobilePrint.Server.Models;
using TaskStatus = MobilePrint.Server.Models.TaskStatus;

namespace MobilePrint.Server.Services;

public class PrintService
{
    private readonly ArchiveManager _archive;
    private readonly PdfRenderer _renderer;
    private readonly OfficeConverter _office;
    public event Action<string>? OnLog;
    private void Log(string msg) { OnLog?.Invoke(msg); _archive.Log(msg); }

    public string PrinterName { get; set; } = "HP LaserJet 1020";

    // 1 毫米 = 100/25.4 个“百分之一英寸”（.NET 打印 Graphics 的默认单位）
    private const double MmToUnit = 100.0 / 25.4;

    public PrintService(ArchiveManager archive, PdfRenderer renderer, OfficeConverter office)
    {
        _archive = archive;
        _renderer = renderer;
        _office = office;
    }

    public bool IsPrinterAvailable()
    {
        try
        {
            return PrinterSettings.InstalledPrinters.Cast<string>()
                .Any(p => p.Equals(PrinterName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public List<string> GetInstalledPrinters()
    {
        var list = new List<string>();
        try { foreach (string p in PrinterSettings.InstalledPrinters) list.Add(p); }
        catch { }
        return list;
    }

    /// <summary>
    /// Prepare task: convert office/image to PDF if needed, resolve page count and selected pages.
    /// </summary>
    public void PrepareTask(PrintTask task)
    {
        string ext = Path.GetExtension(task.FilePath).ToLower();

        if (ext == ".pdf")
        {
            task.PdfPath = task.FilePath;
        }
        else if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" or ".gif")
        {
            // images: no PDF needed; PrintTask will render image directly
            task.PdfPath = null; // signal image mode
        }
        else if (_office.IsOfficeFile(task.FilePath))
        {
            task.Status = TaskStatus.Converting;
            task.PdfPath = _office.ConvertToPdf(task.FilePath);
        }
        else
        {
            throw new InvalidOperationException($"不支持的文件格式: {ext}");
        }

        // determine total pages
        if (task.PdfPath != null && File.Exists(task.PdfPath))
        {
            task.TotalPages = _renderer.GetPageCount(task.PdfPath);
        }
        else
        {
            task.TotalPages = 1; // image
        }

        // parse page range
        var (pages, warning) = PageRangeParser.Parse(task.Parameters.PageRange, task.TotalPages);
        task.SelectedPages = pages;
        if (warning != null) Log(warning);
    }

    /// <summary>
    /// 关键修正：把 Graphics 的原点从“可打印区左上角”平移回“纸张左上角”。
    ///
    /// .NET 的 PrintDocument 默认 OriginAtMargins = false，此时 e.Graphics 的 (0,0)
    /// 位于打印机物理不可打印区（硬边距）的内侧边界，而不是纸张的物理左上角。
    /// 如果直接按纸张坐标（e.PageBounds）绘制整页位图，整幅内容就会向右、向下
    /// 各偏移一个硬边距（HP LaserJet 1020 约 4mm），右下角还会被裁掉——
    /// 这正是“偏下偏右、四边不等”的根因。
    ///
    /// 修正方式：用 PageSettings.HardMarginX/HardMarginY 做一次负向平移，
    /// 使 (0,0) 与纸张物理左上角重合，之后所有坐标都按纸张坐标绘制。
    /// </summary>
    private void ApplyPaperOrigin(Graphics g, PageSettings ps)
    {
        g.PageUnit = GraphicsUnit.Display; // 打印机上 Display = 1/100 英寸

        float hardX = ps.HardMarginX;
        float hardY = ps.HardMarginY;

        // 个别驱动可能返回 0；此时退回到 OriginAtMargins 机制不能保证准确，
        // 所以仅在确实拿到非零硬边距时平移，避免“双重补偿”。
        if (hardX > 0 || hardY > 0)
        {
            if (hardX < 0) hardX = 0;
            if (hardY < 0) hardY = 0;
            g.TranslateTransform(-hardX, -hardY);
        }
    }

    private void LogPageDiagnostics(PageSettings ps, Rectangle pageBounds, RectangleF clip)
    {
        double mmX = 25.4 / 100.0;
        Log($"纸张: {ps.PaperSize.PaperName} {ps.PaperSize.Width}×{ps.PaperSize.Height} (1/100in)" +
            $" = {ps.PaperSize.Width * mmX:F1}×{ps.PaperSize.Height * mmX:F1}mm, 横向={ps.Landscape}");
        Log($"硬边距(物理不可打印): 左 {ps.HardMarginX * mmX:F2}mm, 上 {ps.HardMarginY * mmX:F2}mm");
        Log($"可打印区: {ps.PrintableArea.Width * mmX:F1}×{ps.PrintableArea.Height * mmX:F1}mm");
        Log($"PageBounds: {pageBounds}, Graphics 可见区: {clip}");
    }

    /// <summary>
    /// 按纸张坐标绘制整页位图，1:1 映射到纸张。
    /// 位图本身已是整张 A4，因此内容在纸面上的位置与源 PDF 完全一致（居中）。
    /// </summary>
    private void DrawPageBitmap(Graphics g, Bitmap bmp, Rectangle paper)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(bmp, new RectangleF(paper.X, paper.Y, paper.Width, paper.Height));
    }

    /// <summary>Execute printing for a prepared task.</summary>
    public void ExecutePrint(PrintTask task)
    {
        if (!IsPrinterAvailable())
            throw new InvalidOperationException($"打印机 '{PrinterName}' 不可用");

        Log($"开始打印任务 {task.TaskId}: {task.FileName}, 共{task.SelectedPages.Count}页, {task.Parameters.Copies}份");

        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = PrinterName;
        doc.PrinterSettings.Copies = (short)task.Parameters.Copies;
        doc.DefaultPageSettings.Landscape = task.Parameters.Orientation == Orientation.Landscape;
        doc.DocumentName = $"MobilePrint-{task.FileName}";

        // 显式指定 A4 纸张，避免驱动默认落到 Letter
        foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
        {
            if (ps.PaperName.Contains("A4", StringComparison.OrdinalIgnoreCase) ||
                (ps.Width == 827 && ps.Height == 1169))
            {
                doc.DefaultPageSettings.PaperSize = ps;
                break;
            }
        }
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        int currentPageIdx = 0;
        var pageBitmaps = new List<Bitmap>();

        try
        {
            // build list of bitmaps for selected pages
            if (task.PdfPath != null)
            {
                foreach (int pageNum in task.SelectedPages)
                {
                    pageBitmaps.Add(_renderer.RenderPageToBitmap(task.PdfPath, pageNum - 1, task.Parameters));
                }
            }
            else
            {
                // image mode
                pageBitmaps.Add(_renderer.RenderImageToBitmap(task.FilePath, task.Parameters));
            }

            bool diagnosed = false;

            doc.PrintPage += (sender, e) =>
            {
                if (!diagnosed)
                {
                    LogPageDiagnostics(e.PageSettings, e.PageBounds, e.Graphics.VisibleClipBounds);
                    diagnosed = true;
                }

                if (currentPageIdx < pageBitmaps.Count)
                {
                    ApplyPaperOrigin(e.Graphics, e.PageSettings);
                    DrawPageBitmap(e.Graphics, pageBitmaps[currentPageIdx], e.PageBounds);

                    currentPageIdx++;
                    e.HasMorePages = currentPageIdx < pageBitmaps.Count;
                }
                else
                {
                    e.HasMorePages = false;
                }
            };

            doc.PrintController = new StandardPrintController(); // no status dialog
            doc.Print();
            Log($"打印完成任务 {task.TaskId}");
        }
        finally
        {
            foreach (var b in pageBitmaps) b.Dispose();
        }
    }

    /// <summary>
    /// 打印一张“对齐测试页”，用于客观校验内容是否落在纸面正中。
    /// 页面上包含：纸面边框、正中十字线、四边 10mm 刻度尺、以及距四边各 10mm 的虚线框。
    /// 拿尺子量：十字线交点应正好落在纸面中心（105mm, 148.5mm），四条刻度尺第 1 条刻度距纸边应为 10mm。
    /// </summary>
    public void PrintCalibrationPage()
    {
        if (!IsPrinterAvailable())
            throw new InvalidOperationException($"打印机 '{PrinterName}' 不可用");

        Log("开始打印对齐测试页");

        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = PrinterName;
        doc.DocumentName = "MobilePrint-对齐测试页";

        foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
        {
            if (ps.PaperName.Contains("A4", StringComparison.OrdinalIgnoreCase) ||
                (ps.Width == 827 && ps.Height == 1169))
            {
                doc.DefaultPageSettings.PaperSize = ps;
                break;
            }
        }
        doc.DefaultPageSettings.Landscape = false;
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        bool diagnosed = false;

        doc.PrintPage += (sender, e) =>
        {
            var ps = e.PageSettings;
            if (!diagnosed)
            {
                LogPageDiagnostics(ps, e.PageBounds, e.Graphics.VisibleClipBounds);
                diagnosed = true;
            }

            ApplyPaperOrigin(e.Graphics, ps);

            var paper = e.PageBounds;
            double mmX = 25.4 / 100.0;
            float paperWmm = (float)(paper.Width * mmX);
            float paperHmm = (float)(paper.Height * mmX);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var penThin = new Pen(Color.Black, (float)(0.3 * MmToUnit));
            using var penDashed = new Pen(Color.Black, (float)(0.4 * MmToUnit)) { DashStyle = DashStyle.Dash };
            using var font = new Font("Microsoft YaHei", 9f);
            using var fontBig = new Font("Microsoft YaHei", 12f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.Black);

            // 1) 纸面外框（贴纸边，四角会被物理边距裁掉，属正常）
            g.DrawRectangle(penThin, paper.X, paper.Y, paper.Width - 1, paper.Height - 1);

            // 2) 正中十字线（横贯整张纸，交点 = 纸面中心）
            float cx = paper.X + paper.Width / 2f;
            float cy = paper.Y + paper.Height / 2f;
            g.DrawLine(penDashed, paper.X, cy, paper.X + paper.Width, cy);
            g.DrawLine(penDashed, cx, paper.Y, cx, paper.Y + paper.Height);
            g.DrawEllipse(penThin, cx - 2 * (float)MmToUnit, cy - 2 * (float)MmToUnit,
                4 * (float)MmToUnit, 4 * (float)MmToUnit);

            // 3) 距四边各 10mm 的虚线框
            float inset = 10 * (float)MmToUnit;
            g.DrawRectangle(penDashed, paper.X + inset, paper.Y + inset,
                paper.Width - 2 * inset, paper.Height - 2 * inset);

            // 4) 上边与左边的 10mm 刻度尺
            for (int mm = 10; mm <= (int)paperWmm - 10; mm += 10)
            {
                float x = paper.X + mm * (float)MmToUnit;
                float len = (mm % 50 == 0) ? 6 * (float)MmToUnit : 3 * (float)MmToUnit;
                g.DrawLine(penThin, x, paper.Y, x, paper.Y + len);
                if (mm % 50 == 0)
                    g.DrawString(mm.ToString(), font, brush, x + 0.5f * (float)MmToUnit, paper.Y + 6.5f * (float)MmToUnit);
                if (mm % 50 == 0)
                    g.DrawLine(penThin, x, paper.Y + paper.Height - len, x, paper.Y + paper.Height);
            }
            for (int mm = 10; mm <= (int)paperHmm - 10; mm += 10)
            {
                float y = paper.Y + mm * (float)MmToUnit;
                float len = (mm % 50 == 0) ? 6 * (float)MmToUnit : 3 * (float)MmToUnit;
                g.DrawLine(penThin, paper.X, y, paper.X + len, y);
                if (mm % 50 == 0)
                    g.DrawString(mm.ToString(), font, brush, paper.X + 7 * (float)MmToUnit, y - 2 * (float)MmToUnit);
                if (mm % 50 == 0)
                    g.DrawLine(penThin, paper.X + paper.Width - len, y, paper.X + paper.Width, y);
            }

            // 5) 中央说明文字
            string[] lines =
            {
                "MobilePrint 对齐测试页",
                $"纸张 {paperWmm:F1} × {paperHmm:F1} mm",
                $"硬边距 左 {ps.HardMarginX * mmX:F2}mm / 上 {ps.HardMarginY * mmX:F2}mm",
                "请用尺子量：十字线交点应在纸面正中",
                "（A4 横向 105mm / 纵向 148.5mm）",
                "四条刻度尺第 1 条短刻度应距纸边 10mm",
                "外侧虚线框应距四边各 10mm",
            };
            float lineH = 7 * (float)MmToUnit;
            float startY = cy + 12 * (float)MmToUnit;
            for (int i = 0; i < lines.Length; i++)
            {
                var f = i == 0 ? fontBig : font;
                var sz = g.MeasureString(lines[i], f);
                g.DrawString(lines[i], f, brush, cx - sz.Width / 2f, startY + i * lineH);
            }

            e.HasMorePages = false;
        };

        doc.PrintController = new StandardPrintController();
        doc.Print();
        Log("对齐测试页已发送到打印机");
    }
}
