using System.IO;

namespace MobilePrint.Server.Services;

public class ArchiveManager
{
    public string RootDir { get; private set; } = "";
    public string LogDir { get; private set; } = "";

    public void Init()
    {
        var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        RootDir = Path.Combine(myDocs, "MobilePrintFiles");
        LogDir = Path.Combine(RootDir, "logs");
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(LogDir);
    }

    /// <summary>Save uploaded file, return the saved file path and fileId.</summary>
    public (string filePath, string fileId) SaveUploadedFile(byte[] data, string originalName)
    {
        string dateDir = Path.Combine(RootDir, DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dateDir);
        string stamp = DateTime.Now.ToString("HHmmssfff");
        string safeName = SanitizeFileName(originalName);
        string fileId = Guid.NewGuid().ToString("N")[..12];
        string fileName = $"{stamp}_{safeName}";
        string filePath = Path.Combine(dateDir, fileName);
        // avoid overwrite
        int i = 1;
        while (File.Exists(filePath))
        {
            fileName = $"{stamp}_{i}_{safeName}";
            filePath = Path.Combine(dateDir, fileName);
            i++;
        }
        File.WriteAllBytes(filePath, data);
        return (filePath, fileId);
    }

    /// <summary>Save converted PDF next to original file.</summary>
    public string SaveConvertedPdf(string originalPath, byte[] pdfData)
    {
        string dir = Path.GetDirectoryName(originalPath)!;
        string baseName = Path.GetFileNameWithoutExtension(originalPath);
        string pdfPath = Path.Combine(dir, baseName + "_converted.pdf");
        File.WriteAllBytes(pdfPath, pdfData);
        return pdfPath;
    }

    public string GetTodayLogFile()
    {
        return Path.Combine(LogDir, $"print_{DateTime.Now:yyyy-MM-dd}.log");
    }

    public void Log(string message)
    {
        try
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            File.AppendAllText(GetTodayLogFile(), line + Environment.NewLine);
        }
        catch { }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
