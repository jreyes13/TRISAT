using System.Net.Sockets;
using System.Text;

namespace Trisat.Core.Services;

/// <summary>
/// Async TCP line-stream client for connecting to TRISAT-SDR TCP server (default 127.0.0.1:5006).
/// </summary>
public sealed class TcpTelemetryService : IDisposable
{
    private TcpClient? _client;
    private CancellationTokenSource? _cts;
    private long _linesReceived;

    public bool IsConnected { get; private set; }
    public string RemoteEndpoint { get; private set; } = "127.0.0.1:5006";
    public long LinesReceived => Interlocked.Read(ref _linesReceived);

    public event Action<string>? LineReceived;
    public event Action<string>? StatusChanged;

    public async Task ConnectAsync(string host = "127.0.0.1", int port = 5006)
    {
        Disconnect();
        RemoteEndpoint = $"{host}:{port}";
        _cts = new CancellationTokenSource();

        try
        {
            StatusChanged?.Invoke($"Connecting TCP {RemoteEndpoint}...");
            var client = new TcpClient();
            await client.ConnectAsync(host, port, _cts.Token).ConfigureAwait(false);
            _client = client;
            IsConnected = true;
            StatusChanged?.Invoke($"Connected TCP {RemoteEndpoint}");

            _ = Task.Run(() => ReadLoopAsync(client, _cts.Token));
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusChanged?.Invoke($"TCP Connect Failed: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        try
        {
            _cts?.Cancel();
            _client?.Close();
            _client?.Dispose();
        }
        catch
        {
            // Ignore shutdown errors
        }
        finally
        {
            _client = null;
            _cts?.Dispose();
            _cts = null;
            if (IsConnected)
            {
                IsConnected = false;
                StatusChanged?.Invoke("TCP Disconnected");
            }
        }
    }

    private async Task ReadLoopAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                    break;

                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    Interlocked.Increment(ref _linesReceived);
                    LineReceived?.Invoke(trimmed);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disconnect
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                StatusChanged?.Invoke($"TCP Stream Ended: {ex.Message}");
        }
        finally
        {
            IsConnected = false;
            StatusChanged?.Invoke("TCP Disconnected");
        }
    }

    public void Dispose() => Disconnect();
}
