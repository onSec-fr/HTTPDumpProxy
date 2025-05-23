using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProxyServer
{
    private readonly int _port;
    private readonly string _host;
    private readonly TcpListener _listener;
    private CancellationTokenSource _cts = new();

    public ProxyServer(string host, int port)
    {
        _port = port;
        IPAddress _host = IPAddress.Parse(host);
        _listener = new TcpListener(_host, _port);
    }

    public async Task StartAsync()
    {
        _listener.Start();
        Console.WriteLine($"[*] Proxy started on {_host}:{_port}");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => ProxyHandler.HandleClientAsync(client));
            }
        }
        catch (ObjectDisposedException)
        {
            
        }
    }

    public Task StopAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        return Task.CompletedTask;
    }
}
