using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Trisat.Core.Models;
using Trisat.Core.Parsers;

namespace Trisat.Linux.Services;

public sealed record LinuxSerialPortInfo(
    string PortName,
    string DisplayName,
    bool IsKnownGnssDevice,
    int RecommendedBaudRate,
    string? UsbVidPid = null)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Native Linux TTY serial port manager (/dev/ttyACM*, /dev/ttyUSB*, /dev/rfcomm*, /dev/ttyAMA*)
/// for u-blox ZED-F9P (direct USB-CDC with UBX-RXM-SFRBX decoding), Geneq SXblue (FTDI VCP at 19200 bps),
/// and ESP32 USB serial bridges. Uses Linux sysfs and /dev/serial/by-id to identify hardware automatically.
/// Optionally re-broadcasts received NMEA + $GALINAV to UDP :9090 for LAN/mobile clients.
/// </summary>
public sealed class LinuxSerialService : IDisposable
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

    public static IReadOnlyList<LinuxSerialPortInfo> ScanPorts()
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var byIdNames = ReadSerialByIdMap();

        if (Directory.Exists("/dev"))
        {
            string[] patterns = ["ttyACM*", "ttyUSB*", "ttyAMA*", "rfcomm*"];
            foreach (var pattern in patterns)
            {
                try
                {
                    foreach (var path in Directory.GetFiles("/dev", pattern))
                        candidates.Add(path);
                }
                catch
                {
                    // Ignore restricted directory entries
                }
            }
        }

        // Also include any resolved /dev/serial/by-id targets
        foreach (var target in byIdNames.Keys)
            candidates.Add(target);

        var results = new List<LinuxSerialPortInfo>(candidates.Count);
        foreach (var devPath in candidates)
        {
            var devName = Path.GetFileName(devPath);
            var (vid, pid, manufacturer, product) = ReadSysfsUsbInfo(devName);
            byIdNames.TryGetValue(devPath, out var byIdName);

            results.Add(ClassifyPort(devPath, vid, pid, manufacturer, product, byIdName));
        }

        return results
            .OrderByDescending(p => p.IsKnownGnssDevice)
            .ThenBy(p => p.PortName, StringComparer.Ordinal)
            .ToList();
    }

    public static LinuxSerialPortInfo ClassifyPort(
        string devPath,
        string? vendorId,
        string? productId,
        string? manufacturer,
        string? product,
        string? byIdName)
    {
        string vid = (vendorId ?? string.Empty).Trim().ToLowerInvariant();
        string pid = (productId ?? string.Empty).Trim().ToLowerInvariant();
        string combined = $"{manufacturer} {product} {byIdName} {devPath}".ToLowerInvariant();

        bool isUblox = vid == "1546" || combined.Contains("u-blox") || combined.Contains("ublox") || combined.Contains("gnss");
        bool isSxBlue = (vid == "0403" && pid == "b5a0") ||
                        combined.Contains("sxblue") ||
                        combined.Contains("geneq") ||
                        (vid == "0403" && !isUblox && !combined.Contains("esp32"));
        bool isEsp32 = vid is "10c4" or "1a86" or "303a" ||
                       combined.Contains("cp210") ||
                       combined.Contains("ch340") ||
                       combined.Contains("ch341") ||
                       combined.Contains("silicon labs") ||
                       combined.Contains("espressif") ||
                       combined.Contains("esp32");
        bool isRfcomm = devPath.Contains("rfcomm", StringComparison.OrdinalIgnoreCase);

        bool isKnown = isUblox || isSxBlue || isEsp32;
        int recommendedBaud = isSxBlue && !isUblox && !isEsp32 ? 19200 : 115200;

        string tag = isUblox
            ? "[u-blox ZED-F9P]"
            : isSxBlue
                ? "[FTDI / SXblue]"
                : isEsp32
                    ? "[ESP32 UART]"
                    : isRfcomm
                        ? "[Bluetooth RFCOMM]"
                        : "[TTY]";

        string? friendly = BuildFriendlyName(manufacturer, product, byIdName);
        string? vidPid = !string.IsNullOrEmpty(vid) && !string.IsNullOrEmpty(pid) ? $"{vid}:{pid}" : null;

        string display = string.IsNullOrWhiteSpace(friendly)
            ? $"{devPath} {tag}"
            : $"{devPath} — {friendly} {tag}";

        return new LinuxSerialPortInfo(devPath, display, isKnown, recommendedBaud, vidPid);
    }

    private static string? BuildFriendlyName(string? manufacturer, string? product, string? byIdName)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(manufacturer))
            parts.Add(manufacturer.Trim());
        if (!string.IsNullOrWhiteSpace(product) &&
            !string.Equals(product.Trim(), manufacturer?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(product.Trim());
        }

        if (parts.Count > 0)
            return string.Join(' ', parts);

        if (!string.IsNullOrWhiteSpace(byIdName))
            return byIdName.Replace("usb-", "", StringComparison.OrdinalIgnoreCase).Replace('_', ' ');

        return null;
    }

    private static Dictionary<string, string> ReadSerialByIdMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        const string byIdDir = "/dev/serial/by-id";
        if (!Directory.Exists(byIdDir))
            return map;

        try
        {
            foreach (var linkPath in Directory.GetFiles(byIdDir))
            {
                var info = new FileInfo(linkPath);
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null && target.Exists)
                    map[target.FullName] = Path.GetFileName(linkPath);
            }
        }
        catch
        {
            // Ignore unreadable symlinks
        }

        return map;
    }

    private static (string? Vid, string? Pid, string? Manufacturer, string? Product) ReadSysfsUsbInfo(string ttyName)
    {
        try
        {
            var deviceLink = $"/sys/class/tty/{ttyName}/device";
            if (!Directory.Exists(deviceLink))
                return (null, null, null, null);

            var dir = new DirectoryInfo(deviceLink);
            var resolved = dir.ResolveLinkTarget(returnFinalTarget: true) as DirectoryInfo ?? dir;

            // Walk upward up to 4 levels to find USB device descriptors (idVendor/idProduct)
            var cur = resolved;
            for (int depth = 0; depth < 5 && cur is not null; depth++)
            {
                var vidPath = Path.Combine(cur.FullName, "idVendor");
                var pidPath = Path.Combine(cur.FullName, "idProduct");
                if (File.Exists(vidPath) && File.Exists(pidPath))
                {
                    return (
                        ReadTrimmedFile(vidPath),
                        ReadTrimmedFile(pidPath),
                        ReadTrimmedFile(Path.Combine(cur.FullName, "manufacturer")),
                        ReadTrimmedFile(Path.Combine(cur.FullName, "product"))
                    );
                }
                cur = cur.Parent;
            }
        }
        catch
        {
            // Ignore sysfs inspection errors
        }

        return (null, null, null, null);
    }

    private static string? ReadTrimmedFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
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

            LogMessage?.Invoke($"[TTY] Connected to {ConnectedPortName}");
            ConnectionChanged?.Invoke(true, ConnectedPortName);

            if (autoInitZedF9p)
                _ = ConfigureZedF9pAsync();

            _ = Task.Run(() => ReadLoopAsync(sp, _readCts.Token));
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            LogMessage?.Invoke($"[TTY] Permission denied opening {portName}. Run: sudo usermod -aG dialout $USER (then re-login) or install trisat_linux/scripts/99-trisat-gnss.rules");
            ConnectionChanged?.Invoke(false, "Permission denied (dialout)");
            return false;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[TTY] Failed to open {portName}: {ex.Message}");
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
            LogMessage?.Invoke("[TTY] Connect a ZED-F9P serial port (/dev/ttyACM*) first.");
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
            LogMessage?.Invoke("[TTY] Connect a ZED-F9P serial port (/dev/ttyACM*) first.");
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
            LogMessage?.Invoke("[TTY] Connect an SXblue serial port (/dev/ttyUSB*) first.");
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
                    LogMessage?.Invoke($"[TTY] Read interrupted: {ex.Message}");
                break;
            }
        }
    }

    private void RelayLineIfEnabled(string line)
    {
        if (!RelayToUdp9090) return;

        try
        {
            _udpRelay ??= new UdpClient { EnableBroadcast = true };
            var bytes = Encoding.ASCII.GetBytes(line + "\n");
            _udpRelay.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, 9090));
            _udpRelay.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, 9090));
        }
        catch
        {
            // Non-fatal relay error
        }
    }

    public void Dispose() => Disconnect();
}
