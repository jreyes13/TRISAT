using System.Text;
using Tmds.DBus;

namespace Trisat.Linux.Services;

public sealed record DiscoveredBleDevice(
    string ObjectPath,
    string AddressFormatted,
    string Name,
    short Rssi,
    bool IsTrisatOrGnss)
{
    public string DisplayLabel => $"{(IsTrisatOrGnss ? "★ " : "")}{Name} [{AddressFormatted}] ({Rssi} dBm)";
    public override string ToString() => DisplayLabel;
}

/// <summary>
/// Accumulates BLE Nordic UART (NUS) notification chunks into complete lines,
/// while passing single-packet TRISAT GATT JSON notifications through intact.
/// </summary>
public sealed class BleLineFramer
{
    private readonly StringBuilder _buffer = new();
    private readonly object _sync = new();

    public IReadOnlyList<string> Feed(string chunk, bool isSinglePacketJson)
    {
        if (string.IsNullOrEmpty(chunk))
            return Array.Empty<string>();

        if (isSinglePacketJson)
        {
            var trimmed = chunk.Trim();
            return trimmed.Length > 0 ? [trimmed] : Array.Empty<string>();
        }

        var lines = new List<string>();
        lock (_sync)
        {
            _buffer.Append(chunk);
            var accumulated = _buffer.ToString();
            int lastNewline = accumulated.LastIndexOf('\n');
            if (lastNewline >= 0)
            {
                string ready = accumulated[..lastNewline];
                _buffer.Clear();
                if (lastNewline + 1 < accumulated.Length)
                    _buffer.Append(accumulated[(lastNewline + 1)..]);

                foreach (var line in ready.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = line.Trim();
                    if (t.Length > 0) lines.Add(t);
                }
            }
            else if (_buffer.Length > 4096)
            {
                _buffer.Clear();
            }
        }

        return lines;
    }

    public void Clear()
    {
        lock (_sync) _buffer.Clear();
    }
}

[DBusInterface("org.freedesktop.DBus.ObjectManager")]
public interface IDBusObjectManager : IDBusObject
{
    Task<IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>> GetManagedObjectsAsync();
    Task<IDisposable> WatchInterfacesAddedAsync(
        Action<(ObjectPath objectPath, IDictionary<string, IDictionary<string, object>> interfaces)> handler,
        Action<Exception>? onError = null);
}

[DBusInterface("org.bluez.Adapter1")]
public interface IBluezAdapter1 : IDBusObject
{
    Task StartDiscoveryAsync();
    Task StopDiscoveryAsync();
    Task SetDiscoveryFilterAsync(IDictionary<string, object> properties);
    Task<IDictionary<string, object>> GetAllAsync();
}

[DBusInterface("org.bluez.Device1")]
public interface IBluezDevice1 : IDBusObject
{
    Task ConnectAsync();
    Task DisconnectAsync();
    Task<IDictionary<string, object>> GetAllAsync();
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
}

[DBusInterface("org.bluez.GattCharacteristic1")]
public interface IBluezGattCharacteristic1 : IDBusObject
{
    Task StartNotifyAsync();
    Task StopNotifyAsync();
    Task<IDictionary<string, object>> GetAllAsync();
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
}

/// <summary>
/// Native Linux BlueZ D-Bus (org.bluez) Bluetooth Low Energy client supporting:
///   1. ESP32 Hybrid Bridge Nordic UART Service (NUS: 6E400001-B5A3-F393-E0A9-E50E24DCCA9E)
///   2. TRISAT BLE GATT Service (00001835-0000-1000-8000-00805F9B34FB)
/// </summary>
public sealed class LinuxBleService : IDisposable
{
    public const string BluezServiceName = "org.bluez";

    public static readonly string NordicUartServiceUuid = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
    public static readonly string NordicUartTxCharUuid = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";

    public static readonly string TrisatServiceUuid = "00001835-0000-1000-8000-00805f9b34fb";
    public static readonly string TrisatInavCharUuid = "00002a90-0000-1000-8000-00805f9b34fb";
    public static readonly string TrisatSatStatusCharUuid = "00002a91-0000-1000-8000-00805f9b34fb";
    public static readonly string TrisatLocationCharUuid = "00002a92-0000-1000-8000-00805f9b34fb";

    private Connection? _bus;
    private IBluezAdapter1? _adapter;
    private IBluezDevice1? _connectedDevice;
    private IDisposable? _interfacesAddedSub;
    private IDisposable? _devicePropsSub;
    private readonly List<(IBluezGattCharacteristic1 Char, IDisposable Sub)> _charSubs = [];
    private readonly Dictionary<string, DiscoveredBleDevice> _discovered = new(StringComparer.Ordinal);
    private readonly BleLineFramer _framer = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _scanCts;
    private long _bytesReceived;

    public bool IsScanning { get; private set; }
    public bool IsConnected { get; private set; }
    public string ConnectedDeviceName { get; private set; } = "None";
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public event Action<IReadOnlyList<DiscoveredBleDevice>>? DevicesUpdated;
    public event Action<string>? TelemetryLineReceived;
    public event Action<string>? LogMessage;
    public event Action<bool, string>? ConnectionChanged;

    public async Task StartScanAsync()
    {
        await StopScanAsync().ConfigureAwait(false);
        lock (_sync) _discovered.Clear();

        try
        {
            var bus = await EnsureSystemBusAsync().ConfigureAwait(false);
            var objMgr = bus.CreateProxy<IDBusObjectManager>(BluezServiceName, ObjectPath.Root);
            var objects = await objMgr.GetManagedObjectsAsync().ConfigureAwait(false);

            ObjectPath? adapterPath = objects
                .Where(kv => kv.Value.ContainsKey("org.bluez.Adapter1"))
                .Select(kv => (ObjectPath?)kv.Key)
                .FirstOrDefault();

            if (!adapterPath.HasValue)
            {
                LogMessage?.Invoke("[BLE] No BlueZ Bluetooth adapter found (/org/bluez/hci*). Ensure bluetoothd is running.");
                IsScanning = false;
                return;
            }

            _adapter = bus.CreateProxy<IBluezAdapter1>(BluezServiceName, adapterPath.Value);

            // Seed already-known BlueZ devices
            foreach (var (path, ifaces) in objects)
            {
                if (ifaces.TryGetValue("org.bluez.Device1", out var devProps))
                    TryRegisterBluezDevice(path.ToString(), devProps);
            }
            PublishSnapshot();

            _interfacesAddedSub = await objMgr.WatchInterfacesAddedAsync(evt =>
            {
                if (evt.interfaces.TryGetValue("org.bluez.Device1", out var props))
                {
                    if (TryRegisterBluezDevice(evt.objectPath.ToString(), props))
                        PublishSnapshot();
                }
            }).ConfigureAwait(false);

            try
            {
                await _adapter.SetDiscoveryFilterAsync(new Dictionary<string, object>
                {
                    ["Transport"] = "le"
                }).ConfigureAwait(false);
            }
            catch
            {
                // Some adapters reject Transport=le filter; continue with default discovery
            }

            await _adapter.StartDiscoveryAsync().ConfigureAwait(false);
            IsScanning = true;
            _scanCts = new CancellationTokenSource();
            LogMessage?.Invoke($"[BLE] BlueZ scanning on {adapterPath.Value} for TRISAT-GNSS-BLE (NUS) & BLE GNSS receivers...");

            // Periodically refresh RSSI/Name from ObjectManager during active scan
            _ = Task.Run(() => PollScanLoopAsync(objMgr, _scanCts.Token));
        }
        catch (Exception ex)
        {
            IsScanning = false;
            LogMessage?.Invoke($"[BLE] BlueZ scan failed: {ex.Message}");
        }
    }

    public async Task StopScanAsync()
    {
        try
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;

            _interfacesAddedSub?.Dispose();
            _interfacesAddedSub = null;

            if (_adapter is not null && IsScanning)
                await _adapter.StopDiscoveryAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore stop errors if discovery already stopped
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task<bool> ConnectAsync(DiscoveredBleDevice device)
    {
        await StopScanAsync().ConfigureAwait(false);
        await DisconnectAsync().ConfigureAwait(false);

        try
        {
            var bus = await EnsureSystemBusAsync().ConfigureAwait(false);
            var devPath = new ObjectPath(device.ObjectPath);
            var devProxy = bus.CreateProxy<IBluezDevice1>(BluezServiceName, devPath);

            LogMessage?.Invoke($"[BLE] Connecting via BlueZ to {device.Name} [{device.AddressFormatted}]...");
            await devProxy.ConnectAsync().ConfigureAwait(false);
            _connectedDevice = devProxy;

            _devicePropsSub = await devProxy.WatchPropertiesAsync(changes =>
            {
                foreach (var kv in changes.Changed)
                {
                    if (kv.Key == "Connected" && kv.Value is bool isConn && !isConn)
                    {
                        LogMessage?.Invoke($"[BLE] Peripheral disconnected: {ConnectedDeviceName}");
                        _ = DisconnectAsync();
                    }
                }
            }).ConfigureAwait(false);

            // Wait for BlueZ GATT service resolution (up to 8s)
            bool resolved = false;
            for (int i = 0; i < 40; i++)
            {
                var props = await devProxy.GetAllAsync().ConfigureAwait(false);
                if (props.TryGetValue("ServicesResolved", out var sr) && sr is true)
                {
                    resolved = true;
                    break;
                }
                await Task.Delay(200).ConfigureAwait(false);
            }

            if (!resolved)
            {
                LogMessage?.Invoke("[BLE] BlueZ GATT service resolution timed out.");
                await DisconnectAsync().ConfigureAwait(false);
                return false;
            }

            var objMgr = bus.CreateProxy<IDBusObjectManager>(BluezServiceName, ObjectPath.Root);
            var objects = await objMgr.GetManagedObjectsAsync().ConfigureAwait(false);
            string devPrefix = device.ObjectPath.TrimEnd('/') + "/";

            // Find matching GATT services (NUS or TRISAT) belonging to this device
            var matchingServices = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (path, ifaces) in objects)
            {
                var pathStr = path.ToString();
                if (!pathStr.StartsWith(devPrefix, StringComparison.Ordinal))
                    continue;
                if (!ifaces.TryGetValue("org.bluez.GattService1", out var svcProps))
                    continue;
                if (svcProps.TryGetValue("UUID", out var uuidObj) && uuidObj is string uuidStr)
                {
                    var lower = uuidStr.ToLowerInvariant();
                    if (lower == NordicUartServiceUuid || lower == TrisatServiceUuid)
                        matchingServices.Add(pathStr);
                }
            }

            int subscribedCount = 0;
            foreach (var (path, ifaces) in objects)
            {
                var pathStr = path.ToString();
                if (!pathStr.StartsWith(devPrefix, StringComparison.Ordinal))
                    continue;
                if (!ifaces.TryGetValue("org.bluez.GattCharacteristic1", out var chProps))
                    continue;

                string? svcPath = chProps.TryGetValue("Service", out var svcObj) ? svcObj?.ToString() : null;
                if (svcPath is null || !matchingServices.Contains(svcPath))
                    continue;

                string uuid = chProps.TryGetValue("UUID", out var uObj) ? (uObj?.ToString()?.ToLowerInvariant() ?? string.Empty) : string.Empty;
                string[] flags = chProps.TryGetValue("Flags", out var fObj) && fObj is string[] fa ? fa : Array.Empty<string>();
                bool supportsNotify = flags.Any(f =>
                    f.Equals("notify", StringComparison.OrdinalIgnoreCase) ||
                    f.Equals("indicate", StringComparison.OrdinalIgnoreCase));

                if (!supportsNotify)
                    continue;

                var chProxy = bus.CreateProxy<IBluezGattCharacteristic1>(BluezServiceName, path);
                bool isTrisatJsonChar = uuid == TrisatInavCharUuid || uuid == TrisatSatStatusCharUuid || uuid == TrisatLocationCharUuid;

                var sub = await chProxy.WatchPropertiesAsync(changes =>
                {
                    foreach (var kv in changes.Changed)
                    {
                        if (kv.Key == "Value" && kv.Value is byte[] bytes && bytes.Length > 0)
                            HandleNotificationBytes(bytes, isTrisatJsonChar);
                    }
                }).ConfigureAwait(false);

                await chProxy.StartNotifyAsync().ConfigureAwait(false);
                _charSubs.Add((chProxy, sub));
                subscribedCount++;
                LogMessage?.Invoke($"[BLE] Subscribed to GATT characteristic {uuid}");
            }

            if (subscribedCount == 0)
            {
                LogMessage?.Invoke("[BLE] Connected, but no Nordic UART or TRISAT GATT notify characteristics found.");
                await DisconnectAsync().ConfigureAwait(false);
                return false;
            }

            IsConnected = true;
            ConnectedDeviceName = device.Name;
            ConnectionChanged?.Invoke(true, ConnectedDeviceName);
            LogMessage?.Invoke($"[BLE] Streaming telemetry from {ConnectedDeviceName} ({subscribedCount} channel(s)).");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[BLE] BlueZ connection error: {ex.Message}");
            await DisconnectAsync().ConfigureAwait(false);
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        foreach (var (ch, sub) in _charSubs)
        {
            try { await ch.StopNotifyAsync().ConfigureAwait(false); } catch { }
            try { sub.Dispose(); } catch { }
        }
        _charSubs.Clear();

        _devicePropsSub?.Dispose();
        _devicePropsSub = null;

        if (_connectedDevice is not null)
        {
            try { await _connectedDevice.DisconnectAsync().ConfigureAwait(false); } catch { }
            _connectedDevice = null;
        }

        _framer.Clear();
        IsConnected = false;
        ConnectedDeviceName = "None";
        ConnectionChanged?.Invoke(false, "Disconnected");
    }

    public void Disconnect() => _ = DisconnectAsync();

    private void HandleNotificationBytes(byte[] bytes, bool isTrisatJsonChar)
    {
        Interlocked.Add(ref _bytesReceived, bytes.Length);
        var chunk = Encoding.UTF8.GetString(bytes);
        foreach (var line in _framer.Feed(chunk, isTrisatJsonChar))
            TelemetryLineReceived?.Invoke(line);
    }

    private async Task PollScanLoopAsync(IDBusObjectManager objMgr, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsScanning)
        {
            try
            {
                await Task.Delay(1500, ct).ConfigureAwait(false);
                var objects = await objMgr.GetManagedObjectsAsync().ConfigureAwait(false);
                bool changed = false;
                foreach (var (path, ifaces) in objects)
                {
                    if (ifaces.TryGetValue("org.bluez.Device1", out var props))
                        changed |= TryRegisterBluezDevice(path.ToString(), props);
                }
                if (changed)
                    PublishSnapshot();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient scan poll errors
            }
        }
    }

    private bool TryRegisterBluezDevice(string objectPath, IDictionary<string, object> props)
    {
        string address = props.TryGetValue("Address", out var addrObj) ? addrObj?.ToString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(address))
            return false;

        string name = props.TryGetValue("Name", out var nameObj) ? nameObj?.ToString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(name) && props.TryGetValue("Alias", out var aliasObj))
            name = aliasObj?.ToString() ?? string.Empty;

        short rssi = props.TryGetValue("RSSI", out var rssiObj) && rssiObj is short r ? r : (short)-99;
        string[] uuids = props.TryGetValue("UUIDs", out var uuidsObj) && uuidsObj is string[] ua ? ua : Array.Empty<string>();

        bool hasKnownService = uuids.Any(u =>
            u.Equals(NordicUartServiceUuid, StringComparison.OrdinalIgnoreCase) ||
            u.Equals(TrisatServiceUuid, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(name) && !hasKnownService)
            return false;

        if (string.IsNullOrWhiteSpace(name))
            name = hasKnownService ? "TRISAT BLE Bridge" : address;

        bool isTrisat = hasKnownService ||
                        name.Contains("TRISAT", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("GNSS", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("SXBLUE", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("UBLOX", StringComparison.OrdinalIgnoreCase);

        var entry = new DiscoveredBleDevice(objectPath, address, name, rssi, isTrisat);
        lock (_sync)
        {
            _discovered[objectPath] = entry;
        }
        return true;
    }

    private void PublishSnapshot()
    {
        IReadOnlyList<DiscoveredBleDevice> snapshot;
        lock (_sync)
        {
            snapshot = _discovered.Values
                .OrderByDescending(d => d.IsTrisatOrGnss)
                .ThenByDescending(d => d.Rssi)
                .ToList();
        }
        DevicesUpdated?.Invoke(snapshot);
    }

    private async Task<Connection> EnsureSystemBusAsync()
    {
        if (_bus is not null)
            return _bus;

        var bus = new Connection(Address.System);
        await bus.ConnectAsync().ConfigureAwait(false);
        _bus = bus;
        return bus;
    }

    public void Dispose()
    {
        _ = StopScanAsync();
        _ = DisconnectAsync();
        try { _bus?.Dispose(); } catch { }
        _bus = null;
    }
}
