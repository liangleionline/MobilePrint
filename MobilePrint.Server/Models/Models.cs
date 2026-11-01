namespace MobilePrint.Server.Models;

public enum TaskStatus
{
    Waiting,       // 挂起中
    Converting,    // 转码中（也归入挂起中展示）
    Printing,      // 进行中
    Success,      // 成功
    FailConvert,   // 转码失败
    FailPrint,     // 打印失败
    FailFile,      // 文件非法/损坏
    Cancelled      // 已取消
}

public enum ScaleMode
{
    FitA4,
    Original,
    Custom
}

public enum Orientation
{
    Portrait,
    Landscape
}

public class PrintParameters
{
    public int Copies { get; set; } = 1;
    public Orientation Orientation { get; set; } = Orientation.Portrait;
    public string PageRange { get; set; } = ""; // empty = all; e.g. "1,3,5-7"
    public ScaleMode ScaleMode { get; set; } = ScaleMode.FitA4;
    public int ScalePercent { get; set; } = 100; // for Custom
    public int MarginTop { get; set; } = 10;    // mm
    public int MarginBottom { get; set; } = 10;
    public int MarginLeft { get; set; } = 10;
    public int MarginRight { get; set; } = 10;
}

public class PrintTask
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string FileId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";       // archived original file
    public string? PdfPath { get; set; } = null;     // converted pdf (for office/image)
    public TaskStatus Status { get; set; } = TaskStatus.Waiting;
    public string? FailReason { get; set; } = null;
    public DateTime AddedTime { get; set; } = DateTime.Now;
    public DateTime? FinishedTime { get; set; } = null;
    public PrintParameters Parameters { get; set; } = new();
    public int TotalPages { get; set; } = 0;
    public List<int> SelectedPages { get; set; } = new();
}

public class AppConfig
{
    public int ListenPort { get; set; } = 8899;
    public string AuthToken { get; set; } = "123456";
    public bool AutoCleanArchive { get; set; } = false;
    public int AutoCleanDays { get; set; } = 30;
    public string PrinterName { get; set; } = "HP LaserJet 1020";
}
