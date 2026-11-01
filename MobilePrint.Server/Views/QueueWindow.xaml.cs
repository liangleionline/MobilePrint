using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MobilePrint.Server.Models;
using MobilePrint.Server.Services;
using TaskStatus = MobilePrint.Server.Models.TaskStatus;

namespace MobilePrint.Server.Views;

public partial class QueueWindow : Window
{
    private readonly TaskQueue _queue;
    private readonly ArchiveManager _archive;
    private readonly DispatcherTimer _timer;

    public class QueueItem
    {
        public PrintTask Task { get; set; } = null!;
        public string StatusText { get; set; } = "";
        public string AddedTimeText { get; set; } = "";
        public string DetailText { get; set; } = "";
        public string? ToolTip { get; set; }
        public string GroupKey { get; set; } = "";
    }

    public QueueWindow(TaskQueue queue, ArchiveManager archive)
    {
        InitializeComponent();
        _queue = queue;
        _archive = archive;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (s, e) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void Refresh()
    {
        var (running, pending, finished) = _queue.GetQueueSnapshot();
        var items = new List<QueueItem>();

        if (running != null)
            items.Add(MakeItem(running, "running"));

        foreach (var t in pending.OrderBy(t => t.AddedTime))
            items.Add(MakeItem(t, "pending"));

        foreach (var t in finished.OrderByDescending(t => t.FinishedTime ?? DateTime.MinValue))
            items.Add(MakeItem(t, "finished"));

        QueueList.ItemsSource = items;
    }

    private QueueItem MakeItem(PrintTask t, string group)
    {
        string statusText = t.Status switch
        {
            TaskStatus.Printing => "进行中",
            TaskStatus.Waiting => "挂起中",
            TaskStatus.Converting => "挂起中",
            TaskStatus.Success => "成功",
            TaskStatus.Cancelled => "失败",
            _ => "失败"
        };

        string? tip = (t.Status == TaskStatus.FailConvert || t.Status == TaskStatus.FailPrint ||
                       t.Status == TaskStatus.FailFile || t.Status == TaskStatus.Cancelled)
                      ? t.FailReason ?? "未知错误" : null;

        return new QueueItem
        {
            Task = t,
            StatusText = statusText,
            AddedTimeText = t.AddedTime.ToString("yyyy-MM-dd HH:mm:ss"),
            DetailText = t.FileName,
            ToolTip = tip,
            GroupKey = group
        };
    }

    private void QueueList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (QueueList.SelectedItem is not QueueItem item) return;
        var t = item.Task;
        var menu = new ContextMenu();

        if (item.GroupKey == "running")
        {
            menu.Items.Add(MakeMenuItem("删除所有挂起中的任务", () => _queue.ClearPending()));
            menu.Items.Add(MakeMenuItem("清空此列表（除正在打印）", () => _queue.ClearAllExceptRunning()));
        }
        else if (item.GroupKey == "pending")
        {
            menu.Items.Add(MakeMenuItem("取消任务", () => _queue.CancelPending(t.TaskId)));
            menu.Items.Add(MakeMenuItem("下一个打印", () => _queue.MovePendingToTop(t.TaskId)));
            menu.Items.Add(MakeMenuItem("删除其他等待任务", () => _queue.ClearPendingExcept(t.TaskId)));
            menu.Items.Add(MakeMenuItem("清空列表（除正在打印）", () => _queue.ClearAllExceptRunning()));
        }
        else
        {
            menu.Items.Add(MakeMenuItem("删除任务", () => _queue.DeleteFinished(t.TaskId)));
            menu.Items.Add(MakeMenuItem("重试任务", () => { try { _queue.Reprint(t.FileId, t.Parameters); } catch (Exception ex) { MessageBox.Show(ex.Message); } }));
            menu.Items.Add(MakeMenuItem("删除所有成功任务", () => _queue.DeleteAllFinished(true, false)));
            menu.Items.Add(MakeMenuItem("删除所有失败任务", () => _queue.DeleteAllFinished(false, true)));
            menu.Items.Add(MakeMenuItem("删除所有已完成任务", () => _queue.DeleteAllFinished(false, false)));
            menu.Items.Add(MakeMenuItem("清空列表（除正在打印）", () => _queue.ClearAllExceptRunning()));
        }

        menu.PlacementTarget = QueueList;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private MenuItem MakeMenuItem(string header, Action onClick)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (s, e) => { onClick(); Refresh(); };
        return mi;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
