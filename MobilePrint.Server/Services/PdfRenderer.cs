using PdfiumViewer;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using MobilePrint.Server.Models;

namespace MobilePrint.Server.Services;

public class PdfRenderer
{
    private readonly ArchiveManager _archive;
    public event Action<string>? OnLog;
    private void Log(string msg) { OnLog?.Invoke(msg); _archive.Log(msg); }

    public PdfRenderer(ArchiveManager archive) { _archive = archive; }

    public int GetPageCount(string pdfPath)
    {
        using var doc = PdfDocument.Load(pdfPath);
        return doc.PageCount;
    }

    /// <summary>Convert an image file to a single-page PDF.</summary>
    public string ImageToPdf(string imagePath)
    {
        // Use Pdfium to create a PDF from the image.
        // Simpler approach: use System.Drawing to build a PDF via Pdfium's bitmap.
        // We'll render the image into a PDF by creating a minimal PDF.
        // Actually, PdfiumViewer doesn't support creating PDFs easily.
        // Alternative: use the image directly in print service (render image to printable bitmap).
        // For consistency, we'll save a PDF placeholder and handle image directly in PrintService.
        // Here we just return the image path; PrintService branches on extension.
        return imagePath;
    }

    /// <summary>
    /// Render a single PDF page to a Bitmap, applying orientation, margins, scaling.
    /// Returns a bitmap sized for printing (at ~200 DPI for good quality).
    /// </summary>
    public Bitmap RenderPageToBitmap(string pdfPath, int pageIndex0, PrintParameters p, int dpi = 200)
    {
        using var doc = PdfDocument.Load(pdfPath);
        if (pageIndex0 < 0 || pageIndex0 >= doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex0));

        var pdfPageSize = doc.PageSizes[pageIndex0]; // in points (1/72 inch)

        // A4 dimensions in mm
        double a4W_mm = 210, a4H_mm = 297;
        if (p.Orientation == Orientation.Landscape)
        {
            (a4W_mm, a4H_mm) = (a4H_mm, a4W_mm);
        }

        // printable area in mm (minus margins)
        double printW_mm = a4W_mm - p.MarginLeft - p.MarginRight;
        double printH_mm = a4H_mm - p.MarginTop - p.MarginBottom;
        if (printW_mm < 10) printW_mm = 10;
        if (printH_mm < 10) printH_mm = 10;

        // convert mm to pixels at dpi
        double mmToInch = 1.0 / 25.4;
        int a4W_px = (int)(a4W_mm * mmToInch * dpi);
        int a4H_px = (int)(a4H_mm * mmToInch * dpi);
        int printW_px = (int)(printW_mm * mmToInch * dpi);
        int printH_px = (int)(printH_mm * mmToInch * dpi);
        int marginLeft_px = (int)(p.MarginLeft * mmToInch * dpi);
        int marginTop_px = (int)(p.MarginTop * mmToInch * dpi);

        // PDF page size in points -> inches -> px
        double pdfW_in = pdfPageSize.Width / 72.0;
        double pdfH_in = pdfPageSize.Height / 72.0;

        // scale factor
        double scale;
        if (p.ScaleMode == ScaleMode.Original)
        {
            scale = 1.0;
        }
        else if (p.ScaleMode == ScaleMode.Custom)
        {
            scale = p.ScalePercent / 100.0;
        }
        else // FitA4
        {
            double scaleW = (printW_px / dpi) / pdfW_in;
            double scaleH = (printH_px / dpi) / pdfH_in;
            scale = Math.Min(scaleW, scaleH);
        }

        int contentW_px = (int)(pdfW_in * dpi * scale);
        int contentH_px = (int)(pdfH_in * dpi * scale);

        // create output bitmap at A4 size
        var bmp = new Bitmap(a4W_px, a4H_px, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;

            // render PDF page to a temp bitmap
            int srcW = (int)(pdfW_in * dpi);
            int srcH = (int)(pdfH_in * dpi);
            if (srcW < 1) srcW = 1;
            if (srcH < 1) srcH = 1;

            using var pageBmp = doc.Render(pageIndex0, srcW, srcH, dpi, dpi, false);

            // center content in printable area
            int x = marginLeft_px + (printW_px - contentW_px) / 2;
            int y = marginTop_px + (printH_px - contentH_px) / 2;
            g.DrawImage(pageBmp, x, y, contentW_px, contentH_px);
        }

        return bmp;
    }

    /// <summary>Render an image file to a printable bitmap (applying same margins/scaling).</summary>
    public Bitmap RenderImageToBitmap(string imagePath, PrintParameters p, int dpi = 200)
    {
        using var srcImg = Image.FromFile(imagePath);
        double a4W_mm = 210, a4H_mm = 297;
        if (p.Orientation == Orientation.Landscape) { (a4W_mm, a4H_mm) = (a4H_mm, a4W_mm); }

        double printW_mm = a4W_mm - p.MarginLeft - p.MarginRight;
        double printH_mm = a4H_mm - p.MarginTop - p.MarginBottom;
        if (printW_mm < 10) printW_mm = 10;
        if (printH_mm < 10) printH_mm = 10;

        double mmToInch = 1.0 / 25.4;
        int a4W_px = (int)(a4W_mm * mmToInch * dpi);
        int a4H_px = (int)(a4H_mm * mmToInch * dpi);
        int printW_px = (int)(printW_mm * mmToInch * dpi);
        int printH_px = (int)(printH_mm * mmToInch * dpi);
        int marginLeft_px = (int)(p.MarginLeft * mmToInch * dpi);
        int marginTop_px = (int)(p.MarginTop * mmToInch * dpi);

        double imgW_in = srcImg.Width / (double)dpi; // assume source already ~dpi
        double imgH_in = srcImg.Height / (double)dpi;

        double scale = p.ScaleMode switch
        {
            ScaleMode.Original => 1.0,
            ScaleMode.Custom => p.ScalePercent / 100.0,
            _ => Math.Min((printW_px / dpi) / imgW_in, (printH_px / dpi) / imgH_in)
        };

        int contentW_px = (int)(srcImg.Width * scale);
        int contentH_px = (int)(srcImg.Height * scale);
        if (contentW_px < 1) contentW_px = 1;
        if (contentH_px < 1) contentH_px = 1;

        var bmp = new Bitmap(a4W_px, a4H_px, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int x = marginLeft_px + (printW_px - contentW_px) / 2;
            int y = marginTop_px + (printH_px - contentH_px) / 2;
            g.DrawImage(srcImg, x, y, contentW_px, contentH_px);
        }
        return bmp;
    }

    /// <summary>Render a preview JPEG of one page (lower res).</summary>
    public byte[] RenderPreviewJpeg(string pdfPath, int pageIndex0, PrintParameters p, int maxWidth = 800)
    {
        // render at lower dpi for preview
        double srcW_in, srcH_in;
        using (var doc = PdfDocument.Load(pdfPath))
        {
            var sz = doc.PageSizes[pageIndex0];
            srcW_in = sz.Width / 72.0;
            srcH_in = sz.Height / 72.0;
        }
        int previewDpi = (int)(maxWidth / srcW_in);
        if (previewDpi < 50) previewDpi = 50;
        using var bmp = RenderPageToBitmap(pdfPath, pageIndex0, p, previewDpi);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    public byte[] RenderImagePreviewJpeg(string imagePath, PrintParameters p, int maxWidth = 800)
    {
        using var srcImg = Image.FromFile(imagePath);
        int previewDpi = (int)(maxWidth / (srcImg.Width / 200.0));
        if (previewDpi < 50) previewDpi = 50;
        using var bmp = RenderImageToBitmap(imagePath, p, previewDpi);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }
}
