using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Trisat.Core.Services;

/// <summary>
/// High-performance async UDP telemetry listener (default port 9090, also compatible with TRISAT-SDR :5005)
/// supporting NMEA 0183, TRISAT JSON, and RAW: binary UBX/SXblue datagrams.
/// </summary>
public sealed class UdpTelemetryService : IDisposable
{
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private long _packetsReceived;
    private long _bytesReceived;

    public bool IsListening { get; private set; }
    public int Port { get; private set; } = 9090;
    public long PacketsReceived => Interlocked.Read(ref _packetsReceived);
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public event Action<string, IPEndPoint>? LineReceived;
    public event Action<byte[], IPEndPoint>? RawBinaryReceived;
    public event Action<string>? StatusChanged;

    public void Start(int port = 9090)
    {
        Stop();
        Port = port;

        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));

            _client = new UdpClient { Client = socket };
            _cts = new CancellationTokenSource();
            IsListening = true;
            StatusChanged?.Invoke($"Listening on UDP :{port}");

            _ = Task.Run(() => ReceiveLoopAsync(_client, _cts.Token));
        }
        catch (Exception ex)
        {
            IsListening = false;
            StatusChanged?.Invoke($"UDP Error (:{port}): {ex.Message}");
        }
    }

    public void Stop()
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
            if (IsListening)
            {
                IsListening = false;
                StatusChanged?.Invoke("Disconnected");
            }
        }
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(ct).ConfigureAwait(false);
                var data = result.Buffer;
                if (data.Length == 0) continue;

                Interlocked.Increment(ref _packetsReceived);
                Interlocked.Add(ref _bytesReceived, data.Length);

                // Check for binary RAW: prefix (emitted by f9p_bridge.py and sxblue_reader.py)
                if (data.Length > 4 &&
                    data[0] == (byte)'R' && data[1] == (byte)'A' &&
                    data[2] == (byte)'W' && data[3] == (byte)':')
                {
                    var binaryPayload = new byte[data.Length - 4];
                    Buffer.BlockCopy(data, 4, binaryPayload, 0, binaryPayload.Length);
                    RawBinaryReceived?.Invoke(binaryPayload, result.RemoteEndPoint);
                    continue;
                }

                var text = Encoding.UTF8.GetString(data);
                var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var rawLine in lines)
                {
                    var trimmed = rawLine.Trim();
                    if (trimmed.Length > 0)
                        LineReceived?.Invoke(trimmed, result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                if (!ct.IsCancellationRequested)
                    StatusChanged?.Invoke($"UDP Socket Warning: {ex.Message}");
            }
        }
    }

    public void Dispose() => Stop();
}
