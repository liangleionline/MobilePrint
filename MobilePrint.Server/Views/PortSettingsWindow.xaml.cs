using System.Windows;
using System.Windows.Controls;
using MobilePrint.Server.Models;

namespace MobilePrint.Server.Views;

public partial class PortSettingsWindow : Window
{
    public int NewPort { get; private set; }
    public event Action? OnSaved;

    public PortSettingsWindow(int currentPort)
    {
        InitializeComponent();
        PortBox.Text = currentPort.ToString();
        SaveBtn.Click += (s, e) =>
        {
            if (!int.TryParse(PortBox.Text.Trim(), out int port) || port < 1 || port > 65535)
            {
                MsgText.Text = "请输入1-65535之间的端口号";
                return;
            }
            NewPort = port;
            OnSaved?.Invoke();
            Close();
        };
    }
}
