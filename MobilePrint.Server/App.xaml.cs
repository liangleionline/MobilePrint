using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using System.Text.Json;
using MobilePrint.Server.Models;
using MobilePrint.Server.Services;
using MobilePrint.Server.Views;

namespace MobilePrint.Server;

public partial class App : Application
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    private static void PreloadNativeDlls()
    {
        // Search locations for pdfium.dll
        var candidates = new List<string>();

        // 1. Next to EXE (and x86 subfolder)
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath!) ?? AppContext.BaseDirectory;
        candidates.Add(Path.Combine(exeDir, "pdfium.dll"));
        candidates.Add(Path.Combine(exeDir, "x86", "pdfium.dll"));

        // 2. .NET self-extraction temp dir (recursive search)
        var dotnetTemp = Path.Combine(Path.GetTempPath(), ".net");
        if (Directory.Exists(dotnetTemp))
        {
            try
            {
                foreach (var f in Directory.GetFiles(dotnetTemp, "pdfium.dll", SearchOption.AllDirectories))
                    candidates.Add(f);
            }
            catch { }
        }

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                LoadLibrary(path);
                return;
            }
        }
    }

    private TaskbarIcon? _tray;
    private ArchiveManager? _archive;
    private TaskQueue? _queue;
    private HttpServer? _http;
    private UdpDiscovery? _udp;
    private PrintService? _printService;
    private PdfRenderer? _renderer;
    private OfficeConverter? _office;
    private AppConfig _config = new();
    private QueueWindow? _queueWindow;

    public bool IsServiceRunning => _http != null && _http.IsRunning;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        PreloadNativeDlls();

        // load config - use EXE directory (not temp extraction dir for single-file)
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath!) ?? AppContext.BaseDirectory;
        string configPath = Path.Combine(exeDir, "appconfig.json");
        try
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                _config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            else
            {
                File.WriteAllText(configPath, JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { }

        // init services
        _archive = new ArchiveManager();
        _archive.Init();
        _archive.Log("=== MobilePrint 服务启动 ===");

        _office = new OfficeConverter(_archive);
        _renderer = new PdfRenderer(_archive);
        _printService = new PrintService(_archive, _renderer, _office) { PrinterName = _config.PrinterName };
        _queue = new TaskQueue(_archive, _printService, _renderer, _office);
        _http = new HttpServer(_archive, _queue, _renderer, _config);
        _udp = new UdpDiscovery(_archive) { HttpPort = _config.ListenPort, BroadcastPort = 8081 };

        // tray icon - use the EXE's embedded icon (app.ico)
        _tray = new TaskbarIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            ToolTipText = "MobilePrint 打印服务 v1.1.2"
        };
        var menu = new System.Windows.Controls.ContextMenu();
        var miStart = new System.Windows.Controls.MenuItem { Header = "开启服务" };
        var miStop = new System.Windows.Controls.MenuItem { Header = "停止服务" };
        var miRestart = new System.Windows.Controls.MenuItem { Header = "重启服务" };
        var miQueue = new System.Windows.Controls.MenuItem { Header = "查看队列" };
        var miPort = new System.Windows.Controls.MenuItem { Header = "端口设置" };
        var miExit = new System.Windows.Controls.MenuItem { Header = "退出" };

        miStart.Click += async (s, ev) => await StartServiceAsync();
        miStop.Click += async (s, ev) => await StopServiceAsync();
        miRestart.Click += async (s, ev) => await RestartServiceAsync();
        miQueue.Click += (s, ev) => ShowQueueWindow();
        miPort.Click += (s, ev) => ShowPortSettings();
        miExit.Click += (s, ev) => ExitApp();

        menu.Items.Add(miStart);
        menu.Items.Add(miStop);
        menu.Items.Add(miRestart);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(miQueue);
        menu.Items.Add(miPort);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(miExit);
        _tray.ContextMenu = menu;
        _tray.Visibility = Visibility.Visible;

        // default: start service
        _ = StartServiceAsync();
    }

    private async Task StartServiceAsync()
    {
        try
        {
            _queue?.Start();
            _udp?.Start();
            await (_http?.StartAsync() ?? Task.CompletedTask);
            _tray!.ToolTipText = "MobilePrint 打印服务 (运行中)";
            _archive?.Log("HTTP服务已启动，端口: " + _config.ListenPort);
        }
        catch (Exception ex)
        {
            _archive?.Log("启动失败: " + ex.Message);
            MessageBox.Show("服务启动失败: " + ex.Message, "MobilePrint", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopServiceAsync()
    {
        try
        {
            _udp?.Stop();
            await (_http?.StopAsync() ?? Task.CompletedTask);
            _queue?.Stop();
            _tray!.ToolTipText = "MobilePrint 打印服务 (已停止)";
            _archive?.Log("HTTP服务已停止");
        }
        catch (Exception ex)
        {
            _archive?.Log("停止失败: " + ex.Message);
        }
    }

    private async Task RestartServiceAsync()
    {
        await StopServiceAsync();
        await Task.Delay(500);
        await StartServiceAsync();
    }

    private void ShowQueueWindow()
    {
        if (_queueWindow == null || !_queueWindow.IsVisible)
        {
            _queueWindow = new QueueWindow(_queue!, _archive!);
            _queueWindow.Show();
            _queueWindow.Activate();
        }
        else
        {
            _queueWindow.Activate();
        }
    }

    private async void ShowPortSettings()
    {
        var win = new PortSettingsWindow(_config.ListenPort);
        win.OnSaved += async () =>
        {
            _config.ListenPort = win.NewPort;
            // save config
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath!) ?? AppContext.BaseDirectory;
            string configPath = Path.Combine(exeDir, "appconfig.json");
            File.WriteAllText(configPath, JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));
            _udp!.HttpPort = _config.ListenPort;
            _archive?.Log("端口已改为: " + _config.ListenPort);
            await RestartServiceAsync();
            MessageBox.Show($"端口已改为 {_config.ListenPort}，服务已重启", "MobilePrint", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        win.ShowDialog();
    }

    private async void ExitApp()
    {
        await StopServiceAsync();
        _tray?.Dispose();
        Shutdown();
    }
}
