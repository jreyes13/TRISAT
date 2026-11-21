using System.IO.Ports;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Trisat.Core.Models;
using Trisat.Core.Parsers;

namespace Trisat.Win.Services;

public sealed record WindowsSerialPortInfo(
    string PortName,
    string DisplayName,
    bool IsKnownGnssDevice,
    int RecommendedBaudRate)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Native Windows COM port manager for u-blox ZED-F9P (direct USB-CDC with UBX-RXM-SFRBX decoding),
/// Geneq SXblue (FTDI VCP at 19200 bps), ESP32 USB serial, and Bluetooth SPP COM ports.
/// Optionally re-broadcasts received NMEA + $GALINAV to UDP :9090 for LAN/mobile clients.
/// </summary>
public sealed partial class WindowsSerialService : IDisposable
{
    private SerialPort? _port;
    private CancellationTokenSource? _readCts;
    private UdpClient? _udpRelay;
    private readonly UbxF9pStream _ubxStream = new();

    public bool IsConnected => _port is { IsOpen: true };
    public string ConnectedPortName { get; private set; } = "None";
    public bool RelayToUdp9090 { get; set; }

    public event Action<string>? NmeaLineReceived;
    public event Action<GalileoINavMessage>? InavFrameReceived;
    public event Action<string>? LogMessage;
    public event Action<bool, string>? ConnectionChanged;

    public static IReadOnlyList<WindowsSerialPortInfo> ScanPorts()
    {
        var descriptions = QueryPnpDescriptions();
        var portNames = SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var results = new List<WindowsSerialPortInfo>(portNames.Count);
        foreach (var name in portNames)
        {
            descriptions.TryGetValue(name, out var friendly);
            var lower = (friendly ?? name).ToLowerInvariant();

            bool isUblox = lower.Contains("u-blox") || lower.Contains("ublox") || lower.Contains("gnss");
            bool isSxBlue = lower.Contains("sxblue") || lower.Contains("ftdi") || lower.Contains("usb serial");
            bool isEsp32 = lower.Contains("cp210") || lower.Contains("ch340") || lower.Contains("silicon labs") || lower.Contains("esp32");
            bool isKnown = isUblox || isSxBlue || isEsp32;

            int baud = isSxBlue && !isUblox && !isEsp32 ? 19200 : 115200;
            string tag = isUblox ? "[u-blox ZED-F9P]" : isSxBlue ? "[FTDI / SXblue]" : isEsp32 ? "[ESP32 UART]" : "[COM]";
            string display = string.IsNullOrWhiteSpace(friendly)
                ? $"{name} {tag}"
                : $"{name} — {friendly}";

            results.Add(new WindowsSerialPortInfo(name, display, isKnown, baud));
        }

        return results
            .OrderByDescending(p => p.IsKnownGnssDevice)
            .ThenBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool Connect(string portName, int baudRate = 115200, bool autoInitZedF9p = false)
    {
        Disconnect();

        try
        {
            var sp = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 500,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true,
                ReadBufferSize = 16384
            };
            sp.Open();

            _port = sp;
            ConnectedPortName = $"{portName} @ {baudRate} bps";
            _readCts = new CancellationTokenSource();

            LogMessage?.Invoke($"[COM] Connected to {ConnectedPortName}");
            ConnectionChanged?.Invoke(true, ConnectedPortName);

            if (autoInitZedF9p)
                _ = ConfigureZedF9pAsync();

            _ = Task.Run(() => ReadLoopAsync(sp, _readCts.Token));
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[COM] Failed to open {portName}: {ex.Message}");
            ConnectionChanged?.Invoke(false, "Disconnected");
            return false;
        }
    }

    public void Disconnect()
    {
        try
        {
            _readCts?.Cancel();
            if (_port is { IsOpen: true })
                _port.Close();
            _port?.Dispose();
            _udpRelay?.Dispose();
        }
        catch
        {
            // Ignore close errors on unplugged hardware
        }
        finally
        {
            _port = null;
            _udpRelay = null;
            _readCts?.Dispose();
            _readCts = null;
            ConnectedPortName = "None";
            ConnectionChanged?.Invoke(false, "Disconnected");
        }
    }

    public async Task ConfigureZedF9pAsync()
    {
        if (_port is not { IsOpen: true } sp)
        {
            LogMessage?.Invoke("[COM] Connect a ZED-F9P COM port first.");
            return;
        }

        try
        {
            var commands = UbxF9pStream.BuildEnableNavigationSequence();
            foreach (var cmd in commands)
            {
                await sp.BaseStream.WriteAsync(cmd).ConfigureAwait(false);
                await Task.Delay(30).ConfigureAwait(false);
            }
            await sp.BaseStream.FlushAsync().ConfigureAwait(false);
            LogMessage?.Invoke("[ZED-F9P] Enabled NMEA (GGA/GSV/GSA/GST/RMC) + UBX-RXM-SFRBX Galileo I/NAV.");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[ZED-F9P] Config error: {ex.Message}");
        }
    }

    public async Task FactoryResetZedF9pAsync()
    {
        if (_port is not { IsOpen: true } sp)
        {
            LogMessage?.Invoke("[COM] Connect a ZED-F9P COM port first.");
            return;
        }

        try
        {
            var seq = UbxF9pStream.BuildFactoryResetSequence();
            LogMessage?.Invoke("[ZED-F9P] Starting 5-step factory reset & TRISAT flash optimization...");
            for (int i = 0; i < seq.Count; i++)
            {
                await sp.BaseStream.WriteAsync(seq[i]).ConfigureAwait(false);
                await sp.BaseStream.FlushAsync().ConfigureAwait(false);
                await Task.Delay(i == 0 ? 1500 : 80).ConfigureAwait(false);
            }
            LogMessage?.Invoke("[ZED-F9P] Factory reset & Flash save complete.");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[ZED-F9P] Factory reset error: {ex.Message}");
        }
    }

    public async Task SendSxBlueWakeupAsync()
    {
        if (_port is not { IsOpen: true } sp)
        {
            LogMessage?.Invoke("[COM] Connect an SXblue COM port first.");
            return;
        }

        try
        {
            string[] commands =
            [
                "$JASC,GPGGA,1\r\n",
                "$JASC,GAGSV,1\r\n",
                "$JBIN,94,1\r\n",
                "$JBIN,95,1\r\n",
                "$JSAVE\r\n",
                "$JSHOW\r\n"
            ];

            foreach (var cmd in commands)
            {
                var bytes = Encoding.ASCII.GetBytes(cmd);
                await sp.BaseStream.WriteAsync(bytes).ConfigureAwait(false);
                await Task.Delay(120).ConfigureAwait(false);
            }
            await sp.BaseStream.FlushAsync().ConfigureAwait(false);
            LogMessage?.Invoke("[SXblue] Sent wake-up & $JBIN,94/95 EEPROM configuration commands.");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[SXblue] Command error: {ex.Message}");
        }
    }

    private async Task ReadLoopAsync(SerialPort sp, CancellationToken ct)
    {
        var buffer = new byte[4096];

        while (!ct.IsCancellationRequested && sp.IsOpen)
        {
            try
            {
                int read = await sp.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read <= 0) continue;

                var frames = _ubxStream.Feed(buffer.AsSpan(0, read));
                foreach (var frame in frames)
                {
                    if (frame.Kind == FramedPacketKind.Nmea)
                    {
                        var line = frame.AsAsciiText();
                        NmeaLineReceived?.Invoke(line);
                        RelayLineIfEnabled(line);
                    }
                    else if (frame.Kind == FramedPacketKind.Ubx)
                    {
                        var decoded = UbxF9pStream.TryDecodeGalileoSfrbx(frame.RawBytes);
                        if (decoded.HasValue)
                        {
                            InavFrameReceived?.Invoke(decoded.Value.Message);
                            NmeaLineReceived?.Invoke(decoded.Value.GalinavSentence);
                            RelayLineIfEnabled(decoded.Value.GalinavSentence);
                        }
                    }
                }
            }
            catch (TimeoutException)
            {
                _ = _ubxStream.Feed(ReadOnlySpan<byte>.Empty);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    LogMessage?.Invoke($"[COM] Read interrupted: {ex.Message}");
                break;
            }
        }
    }

    private void RelayLineIfEnabled(string line)
    {
        if (!RelayToUdp9090) return;

        try
        {
            _udpRelay ??= CreateBroadcastUdp();
            var bytes = Encoding.ASCII.GetBytes(line + "\n");
            _udpRelay.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, 9090));
            _udpRelay.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, 9090));
        }
        catch
        {
            // Non-fatal relay error
        }
    }

    private static UdpClient CreateBroadcastUdp()
    {
        var udp = new UdpClient { EnableBroadcast = true };
        return udp;
    }

    private static Dictionary<string, string> QueryPnpDescriptions()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
            return map;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var match = ComPortRegex().Match(name);
                if (match.Success)
                    map[match.Groups[1].Value] = name;
            }
        }
        catch
        {
            // WMI may be unavailable in restricted environments; fallback to raw COM names
        }

        return map;
    }

    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ComPortRegex();

    public void Dispose() => Disconnect();
}
