using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MobilePrint.Server.Services;

public class UdpDiscovery
{
    private readonly ArchiveManager _archive;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    public int BroadcastPort { get; set; } = 8081;
    public int HttpPort { get; set; } = 80;

    public UdpDiscovery(ArchiveManager archive) { _archive = archive; }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, BroadcastPort));
        _udp.EnableBroadcast = true;
        Task.Run(() => ListenLoop(_cts.Token));
        Task.Run(() => BroadcastLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _udp?.Close(); } catch { }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var endpoint = new IPEndPoint(IPAddress.Any, 0);
                var result = await _udp.ReceiveAsync(ct);
                string msg = Encoding.UTF8.GetString(result.Buffer);
                if (msg.Contains("MOBILE_PRINT_DISCOVER"))
                {
                    // respond to discovery request
                    var resp = JsonSerializer.Serialize(new
                    {
                        type = "MOBILE_PRINT_FOUND",
                        port = HttpPort,
                        name = Environment.MachineName
                    });
                    var respBytes = Encoding.UTF8.GetBytes(resp);
                    await _udp.SendAsync(respBytes, respBytes.Length, result.RemoteEndPoint);
                }
            }
            catch { }
        }
    }

    private async Task BroadcastLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var msg = Encoding.UTF8.GetBytes($"MOBILE_PRINT_BEACON|{Environment.MachineName}|{HttpPort}");
                _udp.Send(msg, msg.Length, new IPEndPoint(IPAddress.Broadcast, BroadcastPort));
            }
            catch { }
            try { await Task.Delay(3000, ct); } catch { break; }
        }
    }
}
