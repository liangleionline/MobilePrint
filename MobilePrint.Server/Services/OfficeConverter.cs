using Microsoft.Office.Interop.Word;
using Microsoft.Office.Interop.Excel;
using Microsoft.Office.Interop.PowerPoint;
using System.IO;
using Word = Microsoft.Office.Interop.Word.Application;
using Excel = Microsoft.Office.Interop.Excel.Application;
using PowerPoint = Microsoft.Office.Interop.PowerPoint.Application;

namespace MobilePrint.Server.Services;

public class OfficeConverter
{
    private readonly ArchiveManager _archive;
    public event Action<string>? OnLog;

    public OfficeConverter(ArchiveManager archive) { _archive = archive; }

    private void Log(string msg) { OnLog?.Invoke(msg); _archive.Log(msg); }

    public bool IsOfficeFile(string path)
    {
        string ext = Path.GetExtension(path).ToLower();
        return ext is ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx";
    }

    /// <summary>Convert office document to PDF. Returns pdf path or throws.</summary>
    public string ConvertToPdf(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        Log($"开始Office转PDF: {Path.GetFileName(filePath)}");

        try
        {
            switch (ext)
            {
                case ".doc":
                case ".docx":
                    return ConvertWord(filePath);
                case ".xls":
                case ".xlsx":
                    return ConvertExcel(filePath);
                case ".ppt":
                case ".pptx":
                    return ConvertPowerPoint(filePath);
                default:
                    throw new NotSupportedException($"不支持的Office格式: {ext}");
            }
        }
        catch (Exception ex)
        {
            Log($"Office转PDF失败: {ex.Message}");
            throw new InvalidOperationException($"Office文档转换失败: {ex.Message}", ex);
        }
    }

    private string ConvertWord(string path)
    {
        Word? word = null;
        Document? doc = null;
        try
        {
            word = new Word { Visible = false, DisplayAlerts = WdAlertLevel.wdAlertsNone };
            doc = word.Documents.Open(path, ReadOnly: true, Visible: false);
            string pdfPath = Path.Combine(Path.GetDirectoryName(path)!,
                Path.GetFileNameWithoutExtension(path) + "_converted.pdf");
            doc.SaveAs2(pdfPath, WdSaveFormat.wdFormatPDF);
            return pdfPath;
        }
        finally
        {
            if (doc != null) { doc.Close(false); System.Runtime.InteropServices.Marshal.ReleaseComObject(doc); }
            if (word != null) { word.Quit(); System.Runtime.InteropServices.Marshal.ReleaseComObject(word); }
            GC.Collect(); GC.WaitForPendingFinalizers();
        }
    }

    private string ConvertExcel(string path)
    {
        Excel? excel = null;
        Workbook? wb = null;
        try
        {
            excel = new Excel { Visible = false, DisplayAlerts = false };
            wb = excel.Workbooks.Open(path, ReadOnly: true);
            string pdfPath = Path.Combine(Path.GetDirectoryName(path)!,
                Path.GetFileNameWithoutExtension(path) + "_converted.pdf");
            wb.ExportAsFixedFormat(XlFixedFormatType.xlTypePDF, pdfPath);
            return pdfPath;
        }
        finally
        {
            if (wb != null) { wb.Close(false); System.Runtime.InteropServices.Marshal.ReleaseComObject(wb); }
            if (excel != null) { excel.Quit(); System.Runtime.InteropServices.Marshal.ReleaseComObject(excel); }
            GC.Collect(); GC.WaitForPendingFinalizers();
        }
    }

    private string ConvertPowerPoint(string path)
    {
        dynamic? ppt = null;
        dynamic? pres = null;
        try
        {
            Type pptType = Type.GetTypeFromProgID("PowerPoint.Application")
                ?? throw new InvalidOperationException("未安装 PowerPoint");
            ppt = Activator.CreateInstance(pptType);
            ppt.Visible = 0; // msoFalse
            pres = ppt.Presentations.Open(path, -1, Type.Missing, 0); // ReadOnly=msoTrue(-1), WithWindow=msoFalse(0)
            string pdfPath = Path.Combine(Path.GetDirectoryName(path)!,
                Path.GetFileNameWithoutExtension(path) + "_converted.pdf");
            pres.SaveAs(pdfPath, 32); // PpSaveAsFileType.ppSaveAsPDF = 32
            return pdfPath;
        }
        finally
        {
            if (pres != null) { try { pres.Close(); } catch { } }
            if (ppt != null) { try { ppt.Quit(); } catch { } }
            GC.Collect(); GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>Check if Office is installed (best-effort).</summary>
    public bool IsOfficeAvailable()
    {
        try
        {
            var word = new Word();
            word.Quit();
            return true;
        }
        catch { return false; }
    }
}
