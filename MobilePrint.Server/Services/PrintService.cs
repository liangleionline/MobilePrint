using System.Drawing.Printing;
using System.Drawing;
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

        // Explicitly set paper to A4 (210x297mm = 827x1169 in 0.01 inch units)
        foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
        {
            if (ps.PaperName.Contains("A4", StringComparison.OrdinalIgnoreCase) ||
                (ps.Width == 827 && ps.Height == 1169))
            {
                doc.DefaultPageSettings.PaperSize = ps;
                Log($"纸张: {ps.PaperName} ({ps.Width/100.0*25.4:F0}x{ps.Height/100.0*25.4:F0}mm)");
                break;
            }
        }
        // Set margins to 0 so we control them via the bitmap
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

            doc.PrintPage += (sender, e) =>
            {
                if (currentPageIdx < pageBitmaps.Count)
                {
                    var bmp = pageBitmaps[currentPageIdx];
                    // Draw to FULL physical page (PageBounds), not MarginBounds.
                    // Our bitmap is exactly A4, so it maps 1:1 to the paper.
                    // The printer clips whatever falls outside its hard printable area.
                    // This ensures the content is centered on the physical page.
                    e.Graphics.DrawImage(bmp, e.PageBounds);
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
}
