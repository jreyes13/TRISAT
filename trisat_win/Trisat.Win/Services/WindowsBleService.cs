using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Trisat.Win.Services;

public sealed record DiscoveredBleDevice(
    ulong BluetoothAddress,
    string AddressFormatted,
    string Name,
    short Rssi,
    bool IsTrisatOrGnss)
{
    public string DisplayLabel => $"{(IsTrisatOrGnss ? "★ " : "")}{Name} [{AddressFormatted}] ({Rssi} dBm)";
    public override string ToString() => DisplayLabel;
}

/// <summary>
/// Native Windows 10/11 Bluetooth Low Energy (WinRT) client supporting:
///   1. ESP32 Hybrid Bridge Nordic UART Service (NUS: 6E400001-B5A3-F393-E0A9-E50E24DCCA9E)
///   2. TRISAT BLE GATT Service (00001835-0000-1000-8000-00805F9B34FB)
/// </summary>
public sealed class WindowsBleService : IDisposable
{
    public static readonly Guid NordicUartServiceUuid = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid NordicUartRxCharUuid = Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid NordicUartTxCharUuid = Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");

    public static readonly Guid TrisatServiceUuid = Guid.Parse("00001835-0000-1000-8000-00805F9B34FB");
    public static readonly Guid TrisatInavCharUuid = Guid.Parse("00002A90-0000-1000-8000-00805F9B34FB");
    public static readonly Guid TrisatSatStatusCharUuid = Guid.Parse("00002A91-0000-1000-8000-00805F9B34FB");
    public static readonly Guid TrisatLocationCharUuid = Guid.Parse("00002A92-0000-1000-8000-00805F9B34FB");

    private BluetoothLEAdvertisementWatcher? _watcher;
    private BluetoothLEDevice? _connectedDevice;
    private readonly List<GattCharacteristic> _subscribedChars = [];
    private readonly Dictionary<ulong, DiscoveredBleDevice> _discovered = new();
    private readonly StringBuilder _lineBuffer = new();
    private readonly object _sync = new();
    private long _bytesReceived;

    public bool IsScanning { get; private set; }
    public bool IsConnected { get; private set; }
    public string ConnectedDeviceName { get; private set; } = "None";
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public event Action<IReadOnlyList<DiscoveredBleDevice>>? DevicesUpdated;
    public event Action<string>? TelemetryLineReceived;
    public event Action<string>? LogMessage;
    public event Action<bool, string>? ConnectionChanged;

    public void StartScan()
    {
        StopScan();
        lock (_sync) _discovered.Clear();

        try
        {
            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            watcher.Received += OnAdvertisementReceived;
            watcher.Stopped += (_, args) =>
            {
                IsScanning = false;
                LogMessage?.Invoke($"[BLE] Scan stopped ({args.Error}).");
            };

            watcher.Start();
            _watcher = watcher;
            IsScanning = true;
            LogMessage?.Invoke("[BLE] Scanning for TRISAT-GNSS-BLE (Nordic UART) & BLE GNSS receivers...");
        }
        catch (Exception ex)
        {
            IsScanning = false;
            LogMessage?.Invoke($"[BLE] Scan failed (check Windows Bluetooth adapter): {ex.Message}");
        }
    }

    public void StopScan()
    {
        if (_watcher is not null)
        {
            try
            {
                _watcher.Received -= OnAdvertisementReceived;
                _watcher.Stop();
            }
            catch
            {
                // Ignore watcher stop errors
            }
            _watcher = null;
        }
        IsScanning = false;
    }

    public async Task<bool> ConnectAsync(DiscoveredBleDevice device)
    {
        StopScan();
        Disconnect();

        try
        {
            LogMessage?.Invoke($"[BLE] Connecting to {device.Name} [{device.AddressFormatted}]...");
            var bleDev = await BluetoothLEDevice.FromBluetoothAddressAsync(device.BluetoothAddress);
            if (bleDev is null)
            {
                LogMessage?.Invoke("[BLE] Could not open BluetoothLEDevice handle.");
                return false;
            }

            _connectedDevice = bleDev;
            bleDev.ConnectionStatusChanged += OnConnectionStatusChanged;

            var gattResult = await bleDev.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (gattResult.Status != GattCommunicationStatus.Success)
            {
                LogMessage?.Invoke($"[BLE] GATT discovery failed: {gattResult.Status}");
                Disconnect();
                return false;
            }

            int subscribedCount = 0;
            foreach (var service in gattResult.Services)
            {
                if (service.Uuid != NordicUartServiceUuid && service.Uuid != TrisatServiceUuid)
                    continue;

                var charsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                if (charsResult.Status != GattCommunicationStatus.Success)
                    continue;

                foreach (var ch in charsResult.Characteristics)
                {
                    if (!ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify) &&
                        !ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
                    {
                        continue;
                    }

                    var cccdStatus = await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);

                    if (cccdStatus == GattCommunicationStatus.Success)
                    {
                        ch.ValueChanged += OnCharacteristicValueChanged;
                        _subscribedChars.Add(ch);
                        subscribedCount++;
                        LogMessage?.Invoke($"[BLE] Subscribed to characteristic {ch.Uuid}");
                    }
                }
            }

            if (subscribedCount == 0)
            {
                LogMessage?.Invoke("[BLE] Connected, but no Nordic UART or TRISAT GATT notify characteristics found.");
                Disconnect();
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
            LogMessage?.Invoke($"[BLE] Connection error: {ex.Message}");
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        foreach (var ch in _subscribedChars)
        {
            try
            {
                ch.ValueChanged -= OnCharacteristicValueChanged;
            }
            catch
            {
                // Ignore cleanup error
            }
        }
        _subscribedChars.Clear();

        if (_connectedDevice is not null)
        {
            try
            {
                _connectedDevice.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _connectedDevice.Dispose();
            }
            catch
            {
                // Ignore dispose error
            }
            _connectedDevice = null;
        }

        lock (_sync) _lineBuffer.Clear();
        IsConnected = false;
        ConnectedDeviceName = "None";
        ConnectionChanged?.Invoke(false, "Disconnected");
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var name = args.Advertisement.LocalName;
        var uuids = args.Advertisement.ServiceUuids;
        bool hasKnownService = uuids.Contains(NordicUartServiceUuid) || uuids.Contains(TrisatServiceUuid);

        if (string.IsNullOrWhiteSpace(name) && !hasKnownService)
            return;

        if (string.IsNullOrWhiteSpace(name))
            name = hasKnownService ? "TRISAT BLE Bridge" : "Unknown BLE Device";

        bool isTrisat = hasKnownService ||
                        name.Contains("TRISAT", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("GNSS", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("SXBLUE", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("UBLOX", StringComparison.OrdinalIgnoreCase);

        var entry = new DiscoveredBleDevice(
            BluetoothAddress: args.BluetoothAddress,
            AddressFormatted: FormatMacAddress(args.BluetoothAddress),
            Name: name,
            Rssi: args.RawSignalStrengthInDBm,
            IsTrisatOrGnss: isTrisat);

        IReadOnlyList<DiscoveredBleDevice> snapshot;
        lock (_sync)
        {
            _discovered[args.BluetoothAddress] = entry;
            snapshot = _discovered.Values
                .OrderByDescending(d => d.IsTrisatOrGnss)
                .ThenByDescending(d => d.Rssi)
                .ToList();
        }

        DevicesUpdated?.Invoke(snapshot);
    }

    private void OnCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            if (bytes.Length == 0) return;

            Interlocked.Add(ref _bytesReceived, bytes.Length);
            var chunk = Encoding.UTF8.GetString(bytes);

            // JSON packets on TRISAT GATT characteristics may arrive as a single complete object
            if (sender.Uuid == TrisatInavCharUuid || sender.Uuid == TrisatSatStatusCharUuid || sender.Uuid == TrisatLocationCharUuid)
            {
                var trimmed = chunk.Trim();
                if (trimmed.Length > 0)
                    TelemetryLineReceived?.Invoke(trimmed);
                return;
            }

            // Nordic UART stream: accumulate chunks and split on newlines
            List<string> completeLines = [];
            lock (_sync)
            {
                _lineBuffer.Append(chunk);
                string accumulated = _lineBuffer.ToString();
                int lastNewline = accumulated.LastIndexOf('\n');
                if (lastNewline >= 0)
                {
                    string ready = accumulated[..lastNewline];
                    _lineBuffer.Clear();
                    if (lastNewline + 1 < accumulated.Length)
                        _lineBuffer.Append(accumulated[(lastNewline + 1)..]);

                    foreach (var line in ready.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var t = line.Trim();
                        if (t.Length > 0) completeLines.Add(t);
                    }
                }
                else if (_lineBuffer.Length > 4096)
                {
                    _lineBuffer.Clear();
                }
            }

            foreach (var line in completeLines)
                TelemetryLineReceived?.Invoke(line);
        }
        catch
        {
            // Ignore malformed BLE notification
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            LogMessage?.Invoke($"[BLE] Peripheral disconnected: {ConnectedDeviceName}");
            Disconnect();
        }
    }

    private static string FormatMacAddress(ulong address)
    {
        var hex = address.ToString("X12");
        return $"{hex[0..2]}:{hex[2..4]}:{hex[4..6]}:{hex[6..8]}:{hex[8..10]}:{hex[10..12]}";
    }

    public void Dispose()
    {
        StopScan();
        Disconnect();
    }
}
