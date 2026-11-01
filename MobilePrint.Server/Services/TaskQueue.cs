using System.Collections.Concurrent;
using System.IO;
using MobilePrint.Server.Models;
using TaskStatus = MobilePrint.Server.Models.TaskStatus;

namespace MobilePrint.Server.Services;

public class TaskQueue
{
    private readonly ArchiveManager _archive;
    private readonly PrintService _printService;
    private readonly PdfRenderer _renderer;
    private readonly OfficeConverter _office;

    private readonly object _lock = new();
    private PrintTask? _running;
    private readonly List<PrintTask> _pending = new();
    private readonly List<PrintTask> _finished = new();
    private readonly ConcurrentDictionary<string, PrintTask> _allTasks = new();

    private CancellationTokenSource? _cts;
    private Task? _worker;
    public event Action? OnChanged;

    public TaskQueue(ArchiveManager archive, PrintService printService, PdfRenderer renderer, OfficeConverter office)
    {
        _archive = archive;
        _printService = printService;
        _renderer = renderer;
        _office = office;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => WorkerLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _worker?.Wait(2000); } catch { }
    }

    private async Task WorkerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            PrintTask? next = null;
            lock (_lock)
            {
                if (_running == null && _pending.Count > 0)
                {
                    next = _pending[0];
                    _pending.RemoveAt(0);
                    _running = next;
                    next.Status = TaskStatus.Printing;
                }
            }
            OnChanged?.Invoke();

            if (next == null)
            {
                try { await Task.Delay(500, ct); } catch { break; }
                continue;
            }

            try
            {
                _archive.Log($"处理任务: {next.TaskId} {next.FileName}");
                _printService.PrepareTask(next);
                _printService.ExecutePrint(next);
                next.Status = TaskStatus.Success;
            }
            catch (Exception ex)
            {
                next.FailReason = ex.Message;
                next.Status = next.Status == TaskStatus.Converting ? TaskStatus.FailConvert : TaskStatus.FailPrint;
                _archive.Log($"任务失败 {next.TaskId}: {ex.Message}");
            }
            finally
            {
                next.FinishedTime = DateTime.Now;
                lock (_lock)
                {
                    _finished.Add(next);
                    _running = null;
                }
                OnChanged?.Invoke();
            }
        }
    }

    public PrintTask EnqueueUploadedFile(byte[] data, string fileName, PrintParameters p, out string fileId, out int totalPages)
    {
        var (filePath, fid) = _archive.SaveUploadedFile(data, fileName);
        fileId = fid;
        var task = new PrintTask
        {
            FileId = fid,
            FileName = fileName,
            FilePath = filePath,
            Parameters = p,
            Status = TaskStatus.Waiting
        };

        // pre-compute page count for response
        try
        {
            string ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".pdf")
            {
                task.PdfPath = filePath;
                totalPages = _renderer.GetPageCount(filePath);
            }
            else if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" or ".gif")
            {
                totalPages = 1;
            }
            else if (_office.IsOfficeFile(filePath))
            {
                // don't convert yet; estimate 0 (will be determined during processing)
                totalPages = 0;
            }
            else
            {
                totalPages = 0;
                throw new InvalidOperationException($"不支持的文件格式: {ext}");
            }
        }
        catch
        {
            totalPages = 0;
            throw;
        }

        lock (_lock)
        {
            _pending.Add(task);
        }
        _allTasks[task.TaskId] = task;
        OnChanged?.Invoke();
        return task;
    }

    public PrintTask? GetTask(string taskId) => _allTasks.TryGetValue(taskId, out var t) ? t : null;

    public PrintTask? GetByFileId(string fileId) => _allTasks.Values.FirstOrDefault(t => t.FileId == fileId);

    public bool CancelTask(string taskId)
    {
        lock (_lock)
        {
            var t = _allTasks.Values.FirstOrDefault(x => x.TaskId == taskId);
            if (t == null) return false;
            if (t.Status == TaskStatus.Waiting || t.Status == TaskStatus.Converting)
            {
                _pending.Remove(t);
                t.Status = TaskStatus.Cancelled;
                t.FinishedTime = DateTime.Now;
                _finished.Add(t);
                OnChanged?.Invoke();
                return true;
            }
            if (t.Status == TaskStatus.Printing)
            {
                // best-effort: can't stop hardware once printing
                t.FailReason = "打印机已开始打印，无法终止";
                OnChanged?.Invoke();
                return false;
            }
            return false;
        }
    }

    public PrintTask Reprint(string fileId, PrintParameters p)
    {
        var existing = _allTasks.Values.FirstOrDefault(x => x.FileId == fileId);
        if (existing == null) throw new FileNotFoundException($"文件不存在: {fileId}");
        if (!File.Exists(existing.FilePath)) throw new FileNotFoundException("归档文件已被删除");

        var task = new PrintTask
        {
            FileId = fileId,
            FileName = existing.FileName,
            FilePath = existing.FilePath,
            Parameters = p,
            Status = TaskStatus.Waiting
        };
        lock (_lock) { _pending.Add(task); }
        _allTasks[task.TaskId] = task;
        OnChanged?.Invoke();
        return task;
    }

    // --- Queue window operations ---

    public (PrintTask? running, List<PrintTask> pending, List<PrintTask> finished) GetQueueSnapshot()
    {
        lock (_lock)
        {
            return (_running, new List<PrintTask>(_pending), new List<PrintTask>(_finished));
        }
    }

    public void MovePendingToTop(string taskId)
    {
        lock (_lock)
        {
            var idx = _pending.FindIndex(t => t.TaskId == taskId);
            if (idx <= 0) return;
            var t = _pending[idx];
            _pending.RemoveAt(idx);
            _pending.Insert(0, t);
        }
        OnChanged?.Invoke();
    }

    public void CancelPending(string taskId)
    {
        lock (_lock)
        {
            var t = _pending.FirstOrDefault(x => x.TaskId == taskId);
            if (t == null) return;
            _pending.Remove(t);
            t.Status = TaskStatus.Cancelled;
            t.FinishedTime = DateTime.Now;
            _finished.Add(t);
        }
        OnChanged?.Invoke();
    }

    public void ClearPending()
    {
        lock (_lock)
        {
            foreach (var t in _pending) { t.Status = TaskStatus.Cancelled; t.FinishedTime = DateTime.Now; _finished.Add(t); }
            _pending.Clear();
        }
        OnChanged?.Invoke();
    }

    public void ClearPendingExcept(string taskId)
    {
        lock (_lock)
        {
            var keep = _pending.Where(t => t.TaskId == taskId).ToList();
            var removed = _pending.Where(t => t.TaskId != taskId).ToList();
            _pending.Clear();
            _pending.AddRange(keep);
            foreach (var t in removed) { t.Status = TaskStatus.Cancelled; t.FinishedTime = DateTime.Now; _finished.Add(t); }
        }
        OnChanged?.Invoke();
    }

    public void DeleteFinished(string taskId)
    {
        lock (_lock) { _finished.RemoveAll(t => t.TaskId == taskId); }
        OnChanged?.Invoke();
    }

    public void DeleteAllFinished(bool successOnly, bool failOnly)
    {
        lock (_lock)
        {
            _finished.RemoveAll(t =>
            {
                if (successOnly) return t.Status == TaskStatus.Success;
                if (failOnly) return t.Status == TaskStatus.FailConvert || t.Status == TaskStatus.FailPrint || t.Status == TaskStatus.FailFile || t.Status == TaskStatus.Cancelled;
                return true;
            });
        }
        OnChanged?.Invoke();
    }

    public void ClearAllExceptRunning()
    {
        lock (_lock)
        {
            foreach (var t in _pending) { t.Status = TaskStatus.Cancelled; t.FinishedTime = DateTime.Now; }
            _pending.Clear();
            _finished.Clear();
        }
        OnChanged?.Invoke();
    }
}
