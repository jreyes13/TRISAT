using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using Trisat.Core.Engines;
using Trisat.Core.Models;
using Trisat.Core.Parsers;
using Trisat.Core.Services;
using Trisat.Linux.Services;

namespace Trisat.Linux.ViewModels;

/// <summary>
/// Platform UI interaction callbacks (Avalonia TopLevel StorageProvider &amp; Clipboard).
/// </summary>
public interface IDesktopInteractionHost
{
    Task SetClipboardTextAsync(string text);
    Task<string?> PickOpenFileAsync(string title, string[] patterns);
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string[] patterns);
    Task<string?> PickFolderAsync(string title);
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDesktopInteractionHost? _desktopHost;
    private readonly bool _useUiDispatcher;
    private readonly TleService _tleService = new();
    private readonly OsnmaEngine _osnmaEngine = new();
    private readonly GstClockService _gstClock = new();
    private readonly OsnmaLiveVerifier _liveOsnma;
    private readonly TrisatAssistStore _assistStore = new(retentionSeconds: 60);
    private readonly TrisatAssistServer _assistServer;
    private readonly TrisatAssistClient _assistClient = new();
    private readonly ThreatAnalyzer _threatAnalyzer = new();
    private readonly SatelliteStore _satelliteStore = new(TimeSpan.FromSeconds(30));
    private readonly UdpTelemetryService _udpService = new();
    private readonly TcpTelemetryService _tcpService = new();
    private readonly LinuxSerialService _serialService = new();
    private readonly LinuxBleService _bleService = new();
    private readonly LinuxLocationService _locationService = new();
    private readonly TestTelemetryGenerator _testGenerator = new();
    private readonly ProcessBridgeService _sdrProcess = new();
    private readonly ProcessBridgeService _sbfProcess = new();

    private IReadOnlyList<TleData> _cachedTles = Array.Empty<TleData>();
    private IReadOnlyList<SatelliteInfo> _externalSatellites = Array.Empty<SatelliteInfo>();
    private GnssPosition? _livePosition;
    private DopMetrics? _gsaDop;
    private int _bitwiseTotalChecked;
    private int _bitwiseMatched;
    private int _bitwiseMismatched;
    private int _bitwiseNoRef;
    private BitwiseCheckResult? _lastBitwiseResult;

    public MainViewModel(IDesktopInteractionHost? desktopHost = null, bool autoStartNetwork = true, bool useUiDispatcher = true)
    {
        _desktopHost = desktopHost;
        _useUiDispatcher = useUiDispatcher;
        _liveOsnma = new OsnmaLiveVerifier(_gstClock);
        _assistServer = new TrisatAssistServer(_assistStore, _gstClock, _liveOsnma);
        RepoRootPath = ProcessBridgeService.DiscoverRepositoryRoot() ?? Directory.GetCurrentDirectory();
        PythonExecutable = DetectLinuxPython(RepoRootPath);

        // Initialize commands
        CopyCoordsCommand = new AsyncRelayCommand(CopyCoordinatesAsync, () => EffectivePosition is not null);
        OpenMapsCommand = new RelayCommand(OpenInBrowserMaps, () => EffectivePosition is not null);
        DownloadTleCommand = new AsyncRelayCommand(DownloadTlesAsync);
        ClearNmeaCommand = new RelayCommand(() => NmeaLines.Clear());
        ExportNmeaCommand = new AsyncRelayCommand(ExportNmeaLogAsync, () => NmeaLines.Count > 0);
        ClearInavCommand = new RelayCommand(() => { InavMessages.Clear(); _liveOsnma.Reset(); ResetBitwiseStats(); RefreshThreats(); });
        RunAnnexASelfTestCommand = new RelayCommand(RunAnnexASelfTest);
        ResetThreatSimulationCommand = new RelayCommand(ResetThreatSimulation);
        SyncGstClockCommand = new AsyncRelayCommand(SyncGstClockAsync);
        LoadEuspaXmlKeysCommand = new AsyncRelayCommand(LoadEuspaXmlKeysAsync);
        ToggleAssistServerCommand = new RelayCommand(ToggleAssistServer);
        ToggleAssistClientCommand = new AsyncRelayCommand(ToggleAssistClientAsync);

        RefreshSerialPortsCommand = new RelayCommand(RefreshSerialPorts);
        ToggleSerialConnectionCommand = new RelayCommand(ToggleSerialConnection);
        ConfigureZedF9pCommand = new AsyncRelayCommand(() => _serialService.ConfigureZedF9pAsync());
        FactoryResetZedF9pCommand = new AsyncRelayCommand(() => _serialService.FactoryResetZedF9pAsync());
        SendSxBlueWakeupCommand = new AsyncRelayCommand(() => _serialService.SendSxBlueWakeupAsync());

        ToggleBleScanCommand = new AsyncRelayCommand(ToggleBleScanAsync);
        ConnectBleCommand = new AsyncRelayCommand(ConnectSelectedBleAsync);
        DisconnectBleCommand = new AsyncRelayCommand(() => _bleService.DisconnectAsync());

        ToggleUdpCommand = new RelayCommand(ToggleUdp);
        ToggleTcpCommand = new AsyncRelayCommand(ToggleTcpAsync);
        ToggleTestStreamCommand = new RelayCommand(ToggleTestStream);

        BrowseRepoRootCommand = new AsyncRelayCommand(BrowseRepoRootAsync);
        ToggleSdrCommand = new RelayCommand(ToggleSdrProcess);
        OpenSdrDashboardCommand = new RelayCommand(() => OpenUrl($"http://127.0.0.1:{SdrWebPort}"));

        BrowseSbfFileCommand = new AsyncRelayCommand(async () =>
        {
            var picked = await PickFileAsync("Select Septentrio SBF Recording", ["*.sbf", "*"]);
            if (picked is not null) SbfFilePath = picked;
        });
        BrowseMerkleXmlCommand = new AsyncRelayCommand(async () =>
        {
            var picked = await PickFileAsync("Select EUSPA MerkleTree.xml", ["*.xml", "*"]);
            if (picked is not null) MerkleTreeXmlPath = picked;
        });
        BrowsePublicKeyXmlCommand = new AsyncRelayCommand(async () =>
        {
            var picked = await PickFileAsync("Select EUSPA PublicKey.xml", ["*.xml", "*"]);
            if (picked is not null) PublicKeyXmlPath = picked;
        });
        ToggleSbfReplayCommand = new RelayCommand(ToggleSbfReplay);
        OpenSbfViewerCommand = new RelayCommand(() => OpenUrl($"http://127.0.0.1:{SbfReplayPort}"));

        WireServices();
        LoadInitialState(autoStartNetwork);
    }

    private static string DetectLinuxPython(string repoRoot)
    {
        string[] venvCandidates =
        [
            Path.Combine(repoRoot, ".venv", "bin", "python3"),
            Path.Combine(repoRoot, "trisat_sdr", ".venv", "bin", "python3")
        ];
        foreach (var path in venvCandidates)
        {
            if (File.Exists(path))
                return path;
        }
        return "python3";
    }

    // =========================================================================
    // Collections & Lists
    // =========================================================================

    public ObservableCollection<string> NmeaLines { get; } = [];
    public ObservableCollection<GalileoINavMessage> InavMessages { get; } = [];
    public ObservableCollection<string> SystemLogs { get; } = [];
    public ObservableCollection<string> SdrConsoleLines { get; } = [];
    public ObservableCollection<LinuxSerialPortInfo> AvailableSerialPorts { get; } = [];
    public ObservableCollection<DiscoveredBleDevice> DiscoveredBleDevices { get; } = [];

    public IReadOnlyList<ReferenceSitePreset> SitePresets => ReferenceSitePreset.Defaults;
    public IReadOnlyList<int> BaudRates { get; } = [9600, 19200, 38400, 57600, 115200, 230400, 460800];
    public IReadOnlyList<string> SdrBands { get; } = ["GAL_E1", "GAL_E6", "GAL_E5A", "GAL_E5B", "GAL_E5_ALTBOC", "GPS_L1"];

    // =========================================================================
    // Commands
    // =========================================================================

    public AsyncRelayCommand CopyCoordsCommand { get; }
    public RelayCommand OpenMapsCommand { get; }
    public AsyncRelayCommand DownloadTleCommand { get; }
    public RelayCommand ClearNmeaCommand { get; }
    public AsyncRelayCommand ExportNmeaCommand { get; }
    public RelayCommand ClearInavCommand { get; }
    public RelayCommand RunAnnexASelfTestCommand { get; }
    public RelayCommand ResetThreatSimulationCommand { get; }
    public AsyncRelayCommand SyncGstClockCommand { get; }
    public AsyncRelayCommand LoadEuspaXmlKeysCommand { get; }
    public RelayCommand ToggleAssistServerCommand { get; }
    public AsyncRelayCommand ToggleAssistClientCommand { get; }
    public RelayCommand RefreshSerialPortsCommand { get; }
    public RelayCommand ToggleSerialConnectionCommand { get; }
    public AsyncRelayCommand ConfigureZedF9pCommand { get; }
    public AsyncRelayCommand FactoryResetZedF9pCommand { get; }
    public AsyncRelayCommand SendSxBlueWakeupCommand { get; }
    public AsyncRelayCommand ToggleBleScanCommand { get; }
    public AsyncRelayCommand ConnectBleCommand { get; }
    public AsyncRelayCommand DisconnectBleCommand { get; }
    public RelayCommand ToggleUdpCommand { get; }
    public AsyncRelayCommand ToggleTcpCommand { get; }
    public RelayCommand ToggleTestStreamCommand { get; }
    public AsyncRelayCommand BrowseRepoRootCommand { get; }
    public RelayCommand ToggleSdrCommand { get; }
    public RelayCommand OpenSdrDashboardCommand { get; }
    public AsyncRelayCommand BrowseSbfFileCommand { get; }
    public AsyncRelayCommand BrowseMerkleXmlCommand { get; }
    public AsyncRelayCommand BrowsePublicKeyXmlCommand { get; }
    public RelayCommand ToggleSbfReplayCommand { get; }
    public RelayCommand OpenSbfViewerCommand { get; }

    // =========================================================================
    // Core Receiver Mode & Constellation Filters
    // =========================================================================

    private ReceiverMode _selectedMode = ReceiverMode.ExternalHardware;
    public ReceiverMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(SelectedModeIndex));
                OnPropertyChanged(nameof(IsSimulationMode));
                OnPropertyChanged(nameof(SkySourceLabel));
                OnPropertyChanged(nameof(MapBadgeText));
                if (value == ReceiverMode.InternalPlusTle)
                    _ = _locationService.StartAsync();
                else
                    _locationService.Stop();
                RefreshAllTelemetryViews();
            }
        }
    }

    public int SelectedModeIndex
    {
        get => (int)SelectedMode;
        set => SelectedMode = (ReceiverMode)Math.Clamp(value, 0, 2);
    }

    public bool IsSimulationMode => SelectedMode == ReceiverMode.TleSimulation;

    private ReferenceSitePreset _selectedSitePreset = ReferenceSitePreset.Defaults[0];
    public ReferenceSitePreset SelectedSitePreset
    {
        get => _selectedSitePreset;
        set
        {
            if (SetProperty(ref _selectedSitePreset, value))
                RefreshAllTelemetryViews();
        }
    }

    private double _timeOffsetHours;
    public double TimeOffsetHours
    {
        get => _timeOffsetHours;
        set
        {
            if (SetProperty(ref _timeOffsetHours, value))
            {
                OnPropertyChanged(nameof(TimeOffsetDisplay));
                RefreshAllTelemetryViews();
            }
        }
    }

    public string TimeOffsetDisplay => FormattableString.Invariant($"{TimeOffsetHours:+0.0;-0.0;0.0} h (±12h)");

    private bool _isGalileoEnabled = true;
    public bool IsGalileoEnabled { get => _isGalileoEnabled; set { if (SetProperty(ref _isGalileoEnabled, value)) RefreshAllTelemetryViews(); } }

    private bool _isGpsEnabled = true;
    public bool IsGpsEnabled { get => _isGpsEnabled; set { if (SetProperty(ref _isGpsEnabled, value)) RefreshAllTelemetryViews(); } }

    private bool _isGlonassEnabled = true;
    public bool IsGlonassEnabled { get => _isGlonassEnabled; set { if (SetProperty(ref _isGlonassEnabled, value)) RefreshAllTelemetryViews(); } }

    private bool _isBeidouEnabled = true;
    public bool IsBeidouEnabled { get => _isBeidouEnabled; set { if (SetProperty(ref _isBeidouEnabled, value)) RefreshAllTelemetryViews(); } }

    // =========================================================================
    // Displayed Satellites, Position, DOP & Matrix Properties
    // =========================================================================

    private IReadOnlyList<SatelliteInfo> _displayedSatellites = Array.Empty<SatelliteInfo>();
    public IReadOnlyList<SatelliteInfo> DisplayedSatellites
    {
        get => _displayedSatellites;
        private set
        {
            if (SetProperty(ref _displayedSatellites, value))
            {
                OnPropertyChanged(nameof(VisibleSatelliteCount));
                OnPropertyChanged(nameof(GalileoSummary));
                OnPropertyChanged(nameof(GpsSummary));
                OnPropertyChanged(nameof(GlonassSummary));
                OnPropertyChanged(nameof(BeiDouSummary));
                OnPropertyChanged(nameof(GalileoCount));
                OnPropertyChanged(nameof(GpsCount));
                OnPropertyChanged(nameof(GlonassCount));
                OnPropertyChanged(nameof(BeiDouCount));
            }
        }
    }

    public int VisibleSatelliteCount => DisplayedSatellites.Count;
    public int GalileoCount => DisplayedSatellites.Count(s => s.Constellation == GnssConstellation.Galileo);
    public int GpsCount => DisplayedSatellites.Count(s => s.Constellation == GnssConstellation.Gps);
    public int GlonassCount => DisplayedSatellites.Count(s => s.Constellation == GnssConstellation.Glonass);
    public int BeiDouCount => DisplayedSatellites.Count(s => s.Constellation == GnssConstellation.BeiDou);

    public string GalileoSummary => FormatAvgCno(GnssConstellation.Galileo);
    public string GpsSummary => FormatAvgCno(GnssConstellation.Gps);
    public string GlonassSummary => FormatAvgCno(GnssConstellation.Glonass);
    public string BeiDouSummary => FormatAvgCno(GnssConstellation.BeiDou);

    private string FormatAvgCno(GnssConstellation c)
    {
        var list = DisplayedSatellites.Where(s => s.Constellation == c).ToList();
        return list.Count == 0
            ? "Avg C/N0: -- dB-Hz"
            : FormattableString.Invariant($"Avg C/N0: {list.Average(s => s.Cno):F1} dB-Hz");
    }

    public GnssPosition? EffectivePosition =>
        _livePosition ?? (SelectedMode == ReceiverMode.TleSimulation
            ? new GnssPosition(
                SelectedSitePreset.Latitude,
                SelectedSitePreset.Longitude,
                SelectedSitePreset.AltitudeMeters,
                FixDescription: $"TLE Simulation Base ({SelectedSitePreset.Name})",
                IsSimulated: true)
            : null);

    public double? MapLatitude => EffectivePosition?.Latitude;
    public double? MapLongitude => EffectivePosition?.Longitude;
    public string MapBadgeText => EffectivePosition?.IsSimulated == true ? "TLE SIM SITE" : "GNSS LIVE";

    public string LocationText => EffectivePosition?.FormattedCoordinates ?? "Waiting for GNSS coordinates...";
    public string AccuracyText => EffectivePosition is null
        ? "Connect /dev/tty* / BlueZ BLE / UDP receiver or select TLE Simulation mode"
        : EffectivePosition.IsSimulated
            ? $"{EffectivePosition.FixDescription} · Alt {EffectivePosition.AltitudeMeters:F0} m MSL"
            : $"Fix: {EffectivePosition.FixDescription} | Sats: {NumSatellitesInFix} | HDOP: {Dop.HdopText} | Alt: {EffectivePosition.AltitudeMeters:F1} m";

    private string _copyStatusText = "DISPLAYED POSITION";
    public string CopyStatusText { get => _copyStatusText; set => SetProperty(ref _copyStatusText, value); }

    public bool IsAwaitingLocation => SelectedMode != ReceiverMode.TleSimulation && _livePosition is null && _externalSatellites.Count == 0;
    public bool IsAwaitingTle => (SelectedMode != ReceiverMode.ExternalHardware || _externalSatellites.Count == 0) && _cachedTles.Count == 0;

    public string SkySourceLabel
    {
        get
        {
            if (SelectedMode == ReceiverMode.ExternalHardware && _externalSatellites.Count > 0)
                return _testGenerator.IsRunning ? "SIMULATED STREAM" : "EXTERNAL TELEMETRY";
            return _cachedTles.Count > 0 ? "PREDICTED TLE SKY" : "STANDBY";
        }
    }

    private DopMetrics _dop = new(null, null, null, null);
    public DopMetrics Dop { get => _dop; private set => SetProperty(ref _dop, value); }

    private string _fixTypeDesc = "Autonomous GNSS";
    public string FixTypeDesc { get => _fixTypeDesc; private set => SetProperty(ref _fixTypeDesc, value); }

    private int _numSatellitesInFix;
    public int NumSatellitesInFix { get => _numSatellitesInFix; private set => SetProperty(ref _numSatellitesInFix, value); }

    private string _dgpsStationId = "N/A";
    public string DgpsStationId { get => _dgpsStationId; private set => SetProperty(ref _dgpsStationId, value); }

    private string _dgpsAge = "N/A";
    public string DgpsAge { get => _dgpsAge; private set => SetProperty(ref _dgpsAge, value); }

    private string _gstRmsError = "N/A";
    public string GstRmsError { get => _gstRmsError; private set => SetProperty(ref _gstRmsError, value); }

    private string _activeSourceHeader = "UDP Bridge Listening (:9090) · Ready for /dev/tty* / BlueZ BLE / SDR";
    public string ActiveSourceHeader { get => _activeSourceHeader; private set => SetProperty(ref _activeSourceHeader, value); }

    private bool _pauseNmeaLog;
    public bool PauseNmeaLog { get => _pauseNmeaLog; set => SetProperty(ref _pauseNmeaLog, value); }

    // =========================================================================
    // Threat Analysis & OSNMA Self-Test
    // =========================================================================

    private bool _simJamming;
    public bool SimJamming { get => _simJamming; set { if (SetProperty(ref _simJamming, value)) RefreshAllTelemetryViews(); } }

    private bool _simSpoofing;
    public bool SimSpoofing { get => _simSpoofing; set { if (SetProperty(ref _simSpoofing, value)) RefreshThreats(); } }

    private bool _simMeaconing;
    public bool SimMeaconing { get => _simMeaconing; set { if (SetProperty(ref _simMeaconing, value)) RefreshThreats(); } }

    private bool _simMacTamper;
    public bool SimMacTamper { get => _simMacTamper; set { if (SetProperty(ref _simMacTamper, value)) RefreshThreats(); } }

    private ThreatAssessment _threatAssessment = null!;
    public ThreatAssessment ThreatAssessment { get => _threatAssessment; private set => SetProperty(ref _threatAssessment, value); }

    private OsnmaSelfTestResult? _annexASelfTestResult;
    public OsnmaSelfTestResult? AnnexASelfTestResult
    {
        get => _annexASelfTestResult;
        private set
        {
            if (SetProperty(ref _annexASelfTestResult, value))
                OnPropertyChanged(nameof(AnnexASelfTestBanner));
        }
    }

    public string AnnexASelfTestBanner => AnnexASelfTestResult is null
        ? "Not executed yet · Click 'Run EUSPA Annex A Cryptographic Self-Test' to verify ECDSA P-256, TESLA K14→K0 & HMAC tags"
        : AnnexASelfTestResult.Summary;

    // =========================================================================
    // Live OSNMA, Independent GST Clock & Open-Source TRISAT-Assist
    // =========================================================================

    private string _gstClockBanner = "GST Clock: Unsynced (click Sync SNTP / GST Clock)";
    public string GstClockBanner { get => _gstClockBanner; private set => SetProperty(ref _gstClockBanner, value); }

    private string _liveOsnmaBanner = "Live OSNMA: Awaiting EUSPA XML keys or Assisted OSNMA bootstrap";
    public string LiveOsnmaBanner { get => _liveOsnmaBanner; private set => SetProperty(ref _liveOsnmaBanner, value); }

    private string _bitwiseStatsBanner = "Bitwise Cross-Check: Standby (connect Assisted OSNMA client)";
    public string BitwiseStatsBanner { get => _bitwiseStatsBanner; private set => SetProperty(ref _bitwiseStatsBanner, value); }

    private int _assistServerPort = 8095;
    public int AssistServerPort { get => _assistServerPort; set => SetProperty(ref _assistServerPort, value); }

    private bool _isAssistServerRunning;
    public bool IsAssistServerRunning
    {
        get => _isAssistServerRunning;
        private set
        {
            if (SetProperty(ref _isAssistServerRunning, value))
                OnPropertyChanged(nameof(AssistServerButtonText));
        }
    }
    public string AssistServerButtonText => IsAssistServerRunning ? "Stop Assist Server" : "Start Assist Server";

    private string _assistServerStatusText = "Stopped";
    public string AssistServerStatusText { get => _assistServerStatusText; private set => SetProperty(ref _assistServerStatusText, value); }

    private string _assistClientUrl = "http://127.0.0.1:8095";
    public string AssistClientUrl { get => _assistClientUrl; set => SetProperty(ref _assistClientUrl, value); }

    private bool _isAssistClientConnected;
    public bool IsAssistClientConnected
    {
        get => _isAssistClientConnected;
        private set
        {
            if (SetProperty(ref _isAssistClientConnected, value))
                OnPropertyChanged(nameof(AssistClientButtonText));
        }
    }
    public string AssistClientButtonText => IsAssistClientConnected ? "Disconnect Assist" : "Connect Assist & Bitwise";

    private string _assistClientStatusText = "Disconnected";
    public string AssistClientStatusText { get => _assistClientStatusText; private set => SetProperty(ref _assistClientStatusText, value); }

    // =========================================================================
    // Hardware Controls: Linux TTY Serial, BlueZ BLE, UDP, TCP, Test Stream
    // =========================================================================

    private LinuxSerialPortInfo? _selectedSerialPort;
    public LinuxSerialPortInfo? SelectedSerialPort
    {
        get => _selectedSerialPort;
        set
        {
            if (SetProperty(ref _selectedSerialPort, value) && value is not null)
                SelectedBaudRate = value.RecommendedBaudRate;
        }
    }

    private int _selectedBaudRate = 115200;
    public int SelectedBaudRate { get => _selectedBaudRate; set => SetProperty(ref _selectedBaudRate, value); }

    private bool _autoInitZedF9p = true;
    public bool AutoInitZedF9p { get => _autoInitZedF9p; set => SetProperty(ref _autoInitZedF9p, value); }

    private bool _relaySerialToUdp;
    public bool RelaySerialToUdp
    {
        get => _relaySerialToUdp;
        set
        {
            if (SetProperty(ref _relaySerialToUdp, value))
                _serialService.RelayToUdp9090 = value;
        }
    }

    private bool _isSerialConnected;
    public bool IsSerialConnected
    {
        get => _isSerialConnected;
        private set
        {
            if (SetProperty(ref _isSerialConnected, value))
                OnPropertyChanged(nameof(SerialConnectButtonText));
        }
    }
    public string SerialConnectButtonText => IsSerialConnected ? "Disconnect TTY Port" : "Connect TTY Port";

    private string _serialStatusText = "Disconnected";
    public string SerialStatusText { get => _serialStatusText; private set => SetProperty(ref _serialStatusText, value); }

    private DiscoveredBleDevice? _selectedBleDevice;
    public DiscoveredBleDevice? SelectedBleDevice { get => _selectedBleDevice; set => SetProperty(ref _selectedBleDevice, value); }

    private bool _isBleScanning;
    public bool IsBleScanning
    {
        get => _isBleScanning;
        private set
        {
            if (SetProperty(ref _isBleScanning, value))
                OnPropertyChanged(nameof(BleScanButtonText));
        }
    }
    public string BleScanButtonText => IsBleScanning ? "Stop BLE Scan" : "Scan BlueZ BLE";

    private bool _isBleConnected;
    public bool IsBleConnected { get => _isBleConnected; private set => SetProperty(ref _isBleConnected, value); }

    private string _bleStatusText = "Disconnected";
    public string BleStatusText { get => _bleStatusText; private set => SetProperty(ref _bleStatusText, value); }

    private int _udpPort = 9090;
    public int UdpPort { get => _udpPort; set => SetProperty(ref _udpPort, value); }

    private bool _isUdpListening;
    public bool IsUdpListening
    {
        get => _isUdpListening;
        private set
        {
            if (SetProperty(ref _isUdpListening, value))
                OnPropertyChanged(nameof(UdpButtonText));
        }
    }
    public string UdpButtonText => IsUdpListening ? "Stop UDP Listener" : "Start UDP Listener";

    private string _udpStatusText = "Disconnected";
    public string UdpStatusText { get => _udpStatusText; private set => SetProperty(ref _udpStatusText, value); }

    private long _udpPacketCount;
    public long UdpPacketCount { get => _udpPacketCount; private set => SetProperty(ref _udpPacketCount, value); }

    private string _tcpHost = "127.0.0.1";
    public string TcpHost { get => _tcpHost; set => SetProperty(ref _tcpHost, value); }

    private int _tcpPort = 5006;
    public int TcpPort { get => _tcpPort; set => SetProperty(ref _tcpPort, value); }

    private bool _isTcpConnected;
    public bool IsTcpConnected
    {
        get => _isTcpConnected;
        private set
        {
            if (SetProperty(ref _isTcpConnected, value))
                OnPropertyChanged(nameof(TcpButtonText));
        }
    }
    public string TcpButtonText => IsTcpConnected ? "Disconnect TCP" : "Connect TRISAT-SDR TCP";

    private string _tcpStatusText = "Disconnected";
    public string TcpStatusText { get => _tcpStatusText; private set => SetProperty(ref _tcpStatusText, value); }

    private bool _isTestStreamRunning;
    public bool IsTestStreamRunning
    {
        get => _isTestStreamRunning;
        private set
        {
            if (SetProperty(ref _isTestStreamRunning, value))
                OnPropertyChanged(nameof(TestStreamButtonText));
        }
    }
    public string TestStreamButtonText => IsTestStreamRunning ? "Stop Simulated Stream" : "Start Simulated Stream (Test)";

    // =========================================================================
    // TLE Status
    // =========================================================================

    private string _tleStatusText = "No TLE orbits loaded · Click Download";
    public string TleStatusText { get => _tleStatusText; private set => SetProperty(ref _tleStatusText, value); }

    private string _tleLastDownloadText = "Never downloaded";
    public string TleLastDownloadText { get => _tleLastDownloadText; private set => SetProperty(ref _tleLastDownloadText, value); }

    private string _tleCountsSummary = "Galileo: 0 | GPS: 0 | GLONASS: 0 | BeiDou: 0";
    public string TleCountsSummary { get => _tleCountsSummary; private set => SetProperty(ref _tleCountsSummary, value); }

    // =========================================================================
    // TRISAT-SDR & Septentrio SBF Replay Launcher
    // =========================================================================

    private string _repoRootPath = string.Empty;
    public string RepoRootPath { get => _repoRootPath; set => SetProperty(ref _repoRootPath, value); }

    private string _pythonExecutable = "python3";
    public string PythonExecutable { get => _pythonExecutable; set => SetProperty(ref _pythonExecutable, value); }

    private string _sdrBand1 = "GAL_E1";
    public string SdrBand1 { get => _sdrBand1; set => SetProperty(ref _sdrBand1, value); }

    private string _sdrBand2 = "GAL_E6";
    public string SdrBand2 { get => _sdrBand2; set => SetProperty(ref _sdrBand2, value); }

    private bool _sdrSingleMode;
    public bool SdrSingleMode { get => _sdrSingleMode; set => SetProperty(ref _sdrSingleMode, value); }

    private bool _sdrEmitLegacySimulation;
    public bool SdrEmitLegacySimulation { get => _sdrEmitLegacySimulation; set => SetProperty(ref _sdrEmitLegacySimulation, value); }

    private int _sdrWebPort = 8085;
    public int SdrWebPort { get => _sdrWebPort; set => SetProperty(ref _sdrWebPort, value); }

    private bool _isSdrRunning;
    public bool IsSdrRunning
    {
        get => _isSdrRunning;
        private set
        {
            if (SetProperty(ref _isSdrRunning, value))
                OnPropertyChanged(nameof(SdrButtonText));
        }
    }
    public string SdrButtonText => IsSdrRunning ? "Stop TRISAT-SDR" : "Launch TRISAT-SDR";

    private string _sdrStatusText = "Stopped";
    public string SdrStatusText { get => _sdrStatusText; private set => SetProperty(ref _sdrStatusText, value); }

    private string _sbfFilePath = string.Empty;
    public string SbfFilePath { get => _sbfFilePath; set => SetProperty(ref _sbfFilePath, value); }

    private string _merkleTreeXmlPath = string.Empty;
    public string MerkleTreeXmlPath { get => _merkleTreeXmlPath; set => SetProperty(ref _merkleTreeXmlPath, value); }

    private string _publicKeyXmlPath = string.Empty;
    public string PublicKeyXmlPath { get => _publicKeyXmlPath; set => SetProperty(ref _publicKeyXmlPath, value); }

    private string _osnmaLibPath = string.Empty;
    public string OsnmaLibPath { get => _osnmaLibPath; set => SetProperty(ref _osnmaLibPath, value); }

    private int _sbfReplayPort = 8090;
    public int SbfReplayPort { get => _sbfReplayPort; set => SetProperty(ref _sbfReplayPort, value); }

    private bool _isSbfReplayRunning;
    public bool IsSbfReplayRunning
    {
        get => _isSbfReplayRunning;
        private set
        {
            if (SetProperty(ref _isSbfReplayRunning, value))
                OnPropertyChanged(nameof(SbfReplayButtonText));
        }
    }
    public string SbfReplayButtonText => IsSbfReplayRunning ? "Stop SBF Replay" : "Launch SBF Replay Viewer";

    private string _sbfStatusText = "Stopped";
    public string SbfStatusText { get => _sbfStatusText; private set => SetProperty(ref _sbfStatusText, value); }

    // =========================================================================
    // Initialization & Wiring
    // =========================================================================

    private void WireServices()
    {
        _udpService.StatusChanged += status => RunOnUi(() =>
        {
            UdpStatusText = status;
            IsUdpListening = _udpService.IsListening;
            UpdateActiveSourceHeader();
        });
        _udpService.LineReceived += (line, ep) => RunOnUi(() =>
        {
            UdpPacketCount = _udpService.PacketsReceived;
            IngestTelemetryLine(line, $"UDP {ep.Address}:{ep.Port}");
        });
        _udpService.RawBinaryReceived += (bytes, ep) => RunOnUi(() =>
        {
            UdpPacketCount = _udpService.PacketsReceived;
            var decoded = UbxF9pStream.TryDecodeGalileoSfrbx(bytes);
            if (decoded.HasValue)
            {
                AddInavMessage(decoded.Value.Message);
                AppendNmeaLine(decoded.Value.GalinavSentence);
            }
            else
            {
                AppendSystemLog($"[UDP RAW {ep}] {bytes.Length} bytes: {Convert.ToHexString(bytes.AsSpan(0, Math.Min(16, bytes.Length)))}...");
            }
        });

        _tcpService.StatusChanged += status => RunOnUi(() =>
        {
            TcpStatusText = status;
            IsTcpConnected = _tcpService.IsConnected;
            UpdateActiveSourceHeader();
        });
        _tcpService.LineReceived += line => RunOnUi(() => IngestTelemetryLine(line, $"TCP {_tcpService.RemoteEndpoint}"));

        _serialService.ConnectionChanged += (connected, name) => RunOnUi(() =>
        {
            IsSerialConnected = connected;
            SerialStatusText = connected ? $"Connected: {name}" : name;
            UpdateActiveSourceHeader();
        });
        _serialService.LogMessage += msg => RunOnUi(() => AppendSystemLog(msg));
        _serialService.NmeaLineReceived += line => RunOnUi(() => IngestTelemetryLine(line, _serialService.ConnectedPortName));
        _serialService.InavFrameReceived += msg => RunOnUi(() => AddInavMessage(msg));

        _bleService.DevicesUpdated += devices => RunOnUi(() =>
        {
            var prevPath = SelectedBleDevice?.ObjectPath;
            DiscoveredBleDevices.Clear();
            foreach (var d in devices) DiscoveredBleDevices.Add(d);
            if (prevPath is not null)
                SelectedBleDevice = DiscoveredBleDevices.FirstOrDefault(d => d.ObjectPath == prevPath);
            else if (SelectedBleDevice is null && DiscoveredBleDevices.Count > 0)
                SelectedBleDevice = DiscoveredBleDevices[0];
        });
        _bleService.ConnectionChanged += (connected, name) => RunOnUi(() =>
        {
            IsBleConnected = connected;
            BleStatusText = connected ? $"Connected: {name}" : "Disconnected";
            UpdateActiveSourceHeader();
        });
        _bleService.LogMessage += msg => RunOnUi(() => AppendSystemLog(msg));
        _bleService.TelemetryLineReceived += line => RunOnUi(() => IngestTelemetryLine(line, $"BLE {_bleService.ConnectedDeviceName}"));

        _locationService.PositionUpdated += pos => RunOnUi(() =>
        {
            if (SelectedMode == ReceiverMode.InternalPlusTle || _livePosition is null)
            {
                _livePosition = pos;
                FixTypeDesc = pos.FixDescription;
                RefreshAllTelemetryViews();
            }
        });
        _locationService.StatusMessage += msg => RunOnUi(() => AppendSystemLog($"[LinuxLocation] {msg}"));

        _testGenerator.SentenceGenerated += sentence => RunOnUi(() => IngestTelemetryLine(sentence, "Test Stream (Simulated)", forceSimulated: true));

        _sdrProcess.OutputReceived += line => RunOnUi(() => AppendSdrConsole(line));
        _sdrProcess.StateChanged += (running, status) => RunOnUi(() =>
        {
            IsSdrRunning = running;
            SdrStatusText = status;
        });

        _sbfProcess.OutputReceived += line => RunOnUi(() => AppendSdrConsole(line));
        _sbfProcess.StateChanged += (running, status) => RunOnUi(() =>
        {
            IsSbfReplayRunning = running;
            SbfStatusText = status;
        });

        _assistServer.StatusChanged += status => RunOnUi(() =>
        {
            IsAssistServerRunning = _assistServer.IsRunning;
            AssistServerStatusText = status;
        });
        _assistServer.LogMessage += msg => RunOnUi(() => AppendSystemLog(msg));
    }

    private void LoadInitialState(bool autoStartNetwork)
    {
        var cached = _tleService.LoadCached();
        if (cached is not null && cached.Elements.Count > 0)
            ApplyTleSnapshot(cached);

        RefreshSerialPorts();
        if (autoStartNetwork)
            _udpService.Start(UdpPort);
        RefreshAllTelemetryViews();
        AppendSystemLog("[TRISAT] Native Linux GNSS Cockpit initialized (Ubuntu / X11 & Wayland).");
    }

    // =========================================================================
    // Telemetry Ingestion Pipeline (NMEA 0183 + TRISAT JSON)
    // =========================================================================

    public void IngestTelemetryLine(string line, string sourceName, bool forceSimulated = false)
    {
        AppendNmeaLine(line);

        if (line.StartsWith('{'))
        {
            var json = TrisatJsonParser.TryParse(line);
            if (json is null) return;

            bool isSim = forceSimulated || json.IsSimulated;
            if (json.Position is not null)
            {
                _livePosition = json.Position with { IsSimulated = isSim };
                FixTypeDesc = _livePosition.FixDescription;
            }
            if (json.Dop is not null)
                _gsaDop = json.Dop;

            if (json.Satellites is { Count: > 0 })
                _externalSatellites = _satelliteStore.UpsertRange(json.Satellites);

            if (json.InavMessage is not null)
                AddInavMessage(json.InavMessage with { IsSimulated = isSim });

            if (!string.IsNullOrWhiteSpace(json.EmbeddedNmeaLine))
                IngestNmeaSentence(json.EmbeddedNmeaLine, isSim);

            RefreshAllTelemetryViews();
            return;
        }

        if (line.StartsWith('$'))
        {
            IngestNmeaSentence(line, forceSimulated);
            RefreshAllTelemetryViews();
        }
    }

    private void IngestNmeaSentence(string line, bool isSimulated)
    {
        var gga = NmeaParser.TryParseGga(line);
        if (gga is not null)
        {
            _livePosition = gga with { IsSimulated = isSimulated || gga.IsSimulated };
            FixTypeDesc = _livePosition.FixDescription;
            NumSatellitesInFix = gga.SatellitesInFix;
            DgpsAge = gga.DgpsAge;
            DgpsStationId = gga.DgpsStationId;
            return;
        }

        var gst = NmeaParser.TryParseGst(line);
        if (gst is not null)
        {
            GstRmsError = gst.RmsError;
            return;
        }

        var gsa = NmeaParser.TryParseGsa(line);
        if (gsa is not null)
        {
            _gsaDop = new DopMetrics(gsa.Pdop, gsa.Hdop, gsa.Vdop, null);
            return;
        }

        var gsvSats = NmeaParser.TryParseGsv(line);
        if (gsvSats.Count > 0)
        {
            _externalSatellites = _satelliteStore.UpsertRange(gsvSats);
            return;
        }

        var nav = NmeaParser.TryParseNavigationSentence(line, isSimulated);
        if (nav is not null)
            AddInavMessage(nav);
    }

    // =========================================================================
    // View Refresh & Derived Calculations
    // =========================================================================

    private void RefreshAllTelemetryViews()
    {
        IReadOnlyList<SatelliteInfo> rawSats;
        if (SelectedMode == ReceiverMode.ExternalHardware && _externalSatellites.Count > 0)
        {
            rawSats = _externalSatellites;
        }
        else if (_cachedTles.Count > 0 && EffectivePosition is not null)
        {
            double offset = SelectedMode == ReceiverMode.TleSimulation ? TimeOffsetHours : 0.0;
            rawSats = OrbitPropagator.PropagateToSkyplot(
                _cachedTles, offset, EffectivePosition.Latitude, EffectivePosition.Longitude);
        }
        else
        {
            rawSats = Array.Empty<SatelliteInfo>();
        }

        var filtered = rawSats.Where(s => s.Constellation switch
        {
            GnssConstellation.Galileo => IsGalileoEnabled,
            GnssConstellation.Gps => IsGpsEnabled,
            GnssConstellation.Glonass => IsGlonassEnabled,
            GnssConstellation.BeiDou => IsBeidouEnabled,
            _ => true
        });

        if (SimJamming)
            filtered = filtered.Select(s => s with { Cno = Math.Max(12, s.Cno - 22) });

        DisplayedSatellites = filtered.ToList();

        var computedDop = OrbitPropagator.CalculateDop(DisplayedSatellites);
        Dop = _gsaDop is { Pdop: not null } or { Hdop: not null }
            ? new DopMetrics(
                _gsaDop.Pdop ?? computedDop.Pdop,
                _gsaDop.Hdop ?? computedDop.Hdop,
                _gsaDop.Vdop ?? computedDop.Vdop,
                computedDop.Gdop)
            : computedDop;

        OnPropertyChanged(nameof(EffectivePosition));
        OnPropertyChanged(nameof(MapLatitude));
        OnPropertyChanged(nameof(MapLongitude));
        OnPropertyChanged(nameof(MapBadgeText));
        OnPropertyChanged(nameof(LocationText));
        OnPropertyChanged(nameof(AccuracyText));
        OnPropertyChanged(nameof(IsAwaitingLocation));
        OnPropertyChanged(nameof(IsAwaitingTle));
        OnPropertyChanged(nameof(SkySourceLabel));
        CopyCoordsCommand.RaiseCanExecuteChanged();
        OpenMapsCommand.RaiseCanExecuteChanged();

        RefreshThreats();
    }

    private void RefreshThreats()
    {
        var flags = new ThreatSimulationFlags(SimJamming, SimSpoofing, SimMeaconing, SimMacTamper);
        var gstStatus = _gstClock.GetStatus();
        var liveSummary = _liveOsnma.GetSummary(InavMessages.Any(m => m.Data.Length > 0));
        var bwStats = new BitwiseSessionStats(
            _bitwiseTotalChecked,
            _bitwiseMatched,
            _bitwiseMismatched,
            _bitwiseNoRef,
            _lastBitwiseResult);

        GstClockBanner = gstStatus.Summary;
        LiveOsnmaBanner = liveSummary.DetailText;
        BitwiseStatsBanner = bwStats.TotalChecked == 0
            ? (IsAssistClientConnected
                ? $"Bitwise Cross-Check armed ({AssistClientUrl}) · awaiting 40-bit OSNMA frames"
                : "Bitwise Cross-Check: Standby (connect Assisted OSNMA client)")
            : $"Bitwise 40-bit OSNMA: {bwStats.MatchedCount} matched · {bwStats.MismatchCount} mismatch · {bwStats.NoReferenceCount} no-ref";

        ThreatAssessment = _threatAnalyzer.Evaluate(
            DisplayedSatellites,
            InavMessages,
            GstRmsError,
            DgpsStationId,
            DgpsAge,
            flags,
            liveSummary,
            gstStatus,
            bwStats);
    }

    private void UpdateActiveSourceHeader()
    {
        var parts = new List<string>();
        if (IsSerialConnected) parts.Add($"TTY: {_serialService.ConnectedPortName}");
        if (IsBleConnected) parts.Add($"BLE: {_bleService.ConnectedDeviceName}");
        if (IsTcpConnected) parts.Add($"TCP: {_tcpService.RemoteEndpoint}");
        if (IsUdpListening) parts.Add($"UDP :{UdpPort}");
        if (IsTestStreamRunning) parts.Add("Simulated Test Stream");

        ActiveSourceHeader = parts.Count == 0
            ? "No active hardware transport · Configure /dev/tty*, BlueZ BLE, or UDP in Settings"
            : "Active Transports: " + string.Join("  |  ", parts);
    }

    // =========================================================================
    // Command Handlers
    // =========================================================================

    private async Task CopyCoordinatesAsync()
    {
        if (EffectivePosition is null) return;
        var text = FormattableString.Invariant($"{EffectivePosition.Latitude:F6}, {EffectivePosition.Longitude:F6}");
        if (_desktopHost is not null)
            await _desktopHost.SetClipboardTextAsync(text);
        CopyStatusText = "COPIED!";
        _ = Task.Delay(2000).ContinueWith(_ => RunOnUi(() => CopyStatusText = "DISPLAYED POSITION"));
    }

    private void OpenInBrowserMaps()
    {
        if (EffectivePosition is null) return;
        var url = FormattableString.Invariant(
            $"https://www.openstreetmap.org/?mlat={EffectivePosition.Latitude:F6}&mlon={EffectivePosition.Longitude:F6}#map=15/{EffectivePosition.Latitude:F6}/{EffectivePosition.Longitude:F6}");
        OpenUrl(url);
    }

    private async Task DownloadTlesAsync()
    {
        TleStatusText = "Downloading fresh TLE orbits from CelesTrak...";
        AppendSystemLog("[TLE] Fetching Galileo, GPS, GLONASS & BeiDou orbits from celestrak.org...");
        var snap = await _tleService.FetchAllOrbitsAsync();
        RunOnUi(() =>
        {
            if (snap is not null)
            {
                ApplyTleSnapshot(snap);
                AppendSystemLog($"[TLE] Loaded {snap.Elements.Count} satellites from CelesTrak.");
                RefreshAllTelemetryViews();
            }
            else
            {
                TleStatusText = "Download failed · Check internet connection";
                AppendSystemLog("[TLE] CelesTrak download failed.");
            }
        });
    }

    private void ApplyTleSnapshot(TleCacheSnapshot snapshot)
    {
        _cachedTles = snapshot.Elements;
        int gal = _cachedTles.Count(t => t.Constellation == "GALILEO");
        int gps = _cachedTles.Count(t => t.Constellation == "GPS");
        int glo = _cachedTles.Count(t => t.Constellation == "GLONASS");
        int bds = _cachedTles.Count(t => t.Constellation == "BEIDOU");

        TleStatusText = $"TLE Orbits Loaded ({_cachedTles.Count} Satellites)";
        TleLastDownloadText = snapshot.DownloadedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        TleCountsSummary = $"Galileo: {gal} | GPS: {gps} | GLONASS: {glo} | BeiDou: {bds}";
    }

    private void RunAnnexASelfTest()
    {
        AnnexASelfTestResult = _osnmaEngine.RunAnnexASelfTest();
        AppendSystemLog($"[OSNMA] {AnnexASelfTestResult.Summary}");
    }

    private async Task SyncGstClockAsync()
    {
        GstClockBanner = "Synchronizing independent GST via SNTP / HTTPS...";
        var status = await _gstClock.SyncWithNetworkAsync();
        RunOnUi(() =>
        {
            GstClockBanner = status.Summary;
            AppendSystemLog($"[GST] {status.Summary}");
            RefreshThreats();
        });
    }

    private async Task LoadEuspaXmlKeysAsync()
    {
        if (string.IsNullOrWhiteSpace(MerkleTreeXmlPath) && string.IsNullOrWhiteSpace(PublicKeyXmlPath))
        {
            var pk = await PickFileAsync("Select EUSPA OSNMA Public Key XML", ["*.xml", "*"]);
            if (pk is not null) PublicKeyXmlPath = pk;
        }

        if (string.IsNullOrWhiteSpace(MerkleTreeXmlPath) && string.IsNullOrWhiteSpace(PublicKeyXmlPath))
        {
            AppendSystemLog("[OSNMA] Select an EUSPA MerkleTree.xml and/or PublicKey.xml file first.");
            return;
        }

        try
        {
            var mat = _liveOsnma.LoadEuspaXmlKeys(
                string.IsNullOrWhiteSpace(MerkleTreeXmlPath) ? null : MerkleTreeXmlPath,
                string.IsNullOrWhiteSpace(PublicKeyXmlPath) ? null : PublicKeyXmlPath);
            _assistStore.UpdateAssistMaterial(mat);
            AppendSystemLog($"[OSNMA] Loaded EUSPA XML keys (PKID={mat.PublicKeyId?.ToString() ?? "--"}, MerkleRoot={(mat.MerkleRootHex is not null ? mat.MerkleRootHex[..12] + "..." : "--")}).");
            RefreshThreats();
        }
        catch (Exception ex)
        {
            AppendSystemLog($"[OSNMA] Failed to load EUSPA XML: {ex.Message}");
        }
    }

    private void ToggleAssistServer()
    {
        if (_assistServer.IsRunning)
        {
            _assistServer.Stop();
            return;
        }

        try
        {
            _assistServer.Start(AssistServerPort);
        }
        catch (Exception ex)
        {
            AppendSystemLog($"[Assisted OSNMA] Could not start server on :{AssistServerPort}: {ex.Message}");
        }
    }

    private async Task ToggleAssistClientAsync()
    {
        if (IsAssistClientConnected)
        {
            IsAssistClientConnected = false;
            AssistClientStatusText = "Disconnected";
            AppendSystemLog("[Assisted OSNMA] Client disconnected.");
            RefreshThreats();
            return;
        }

        var url = AssistClientUrl.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            AppendSystemLog("[Assisted OSNMA] Enter a valid server URL (e.g. http://127.0.0.1:8095).");
            return;
        }

        AssistClientStatusText = $"Connecting to {url}...";
        try
        {
            var remoteGst = await _assistClient.FetchGstAsync(url);
            if (remoteGst is not null && DateTimeOffset.TryParse(remoteGst.UtcIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var netUtc))
                _gstClock.ApplyExternalNetworkUtc(netUtc, $"Assisted OSNMA ({url})");

            var assistMat = await _assistClient.FetchAssistMaterialAsync(url);
            if (assistMat is not null)
                _liveOsnma.ApplyAssistMaterial(assistMat, isAssistedSource: true);

            RunOnUi(() =>
            {
                IsAssistClientConnected = true;
                AssistClientStatusText = $"Connected ({url})";
                AppendSystemLog($"[Assisted OSNMA] Connected to {url} (GST synced, Assisted OSNMA & 2s Bitwise cross-check enabled).");
                RefreshThreats();
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                IsAssistClientConnected = false;
                AssistClientStatusText = "Connection failed";
                AppendSystemLog($"[Assisted OSNMA] Client connect failed: {ex.Message}");
                RefreshThreats();
            });
        }
    }

    private void ResetBitwiseStats()
    {
        _bitwiseTotalChecked = 0;
        _bitwiseMatched = 0;
        _bitwiseMismatched = 0;
        _bitwiseNoRef = 0;
        _lastBitwiseResult = null;
    }

    private void ResetThreatSimulation()
    {
        SimJamming = false;
        SimSpoofing = false;
        SimMeaconing = false;
        SimMacTamper = false;
    }

    private void RefreshSerialPorts()
    {
        var prev = SelectedSerialPort?.PortName;
        AvailableSerialPorts.Clear();
        foreach (var p in LinuxSerialService.ScanPorts())
            AvailableSerialPorts.Add(p);

        SelectedSerialPort = prev is not null
            ? AvailableSerialPorts.FirstOrDefault(p => p.PortName == prev) ?? AvailableSerialPorts.FirstOrDefault()
            : AvailableSerialPorts.FirstOrDefault();

        AppendSystemLog($"[TTY] Discovered {AvailableSerialPorts.Count} Linux serial port(s).");
    }

    private void ToggleSerialConnection()
    {
        if (IsSerialConnected)
        {
            _serialService.Disconnect();
            return;
        }
        if (SelectedSerialPort is null)
        {
            AppendSystemLog("[TTY] No serial port selected. Plug in USB receiver and click Refresh.");
            return;
        }

        _serialService.RelayToUdp9090 = RelaySerialToUdp;
        _serialService.Connect(SelectedSerialPort.PortName, SelectedBaudRate, AutoInitZedF9p);
    }

    private async Task ToggleBleScanAsync()
    {
        if (IsBleScanning)
        {
            await _bleService.StopScanAsync();
            RunOnUi(() => IsBleScanning = false);
        }
        else
        {
            await _bleService.StartScanAsync();
            RunOnUi(() => IsBleScanning = _bleService.IsScanning);
        }
    }

    private async Task ConnectSelectedBleAsync()
    {
        if (SelectedBleDevice is null)
        {
            AppendSystemLog("[BLE] Select a discovered BlueZ BLE device first.");
            return;
        }
        IsBleScanning = false;
        await _bleService.ConnectAsync(SelectedBleDevice);
    }

    private void ToggleUdp()
    {
        if (IsUdpListening)
            _udpService.Stop();
        else
            _udpService.Start(UdpPort);
    }

    private async Task ToggleTcpAsync()
    {
        if (IsTcpConnected)
            _tcpService.Disconnect();
        else
            await _tcpService.ConnectAsync(TcpHost, TcpPort);
    }

    private void ToggleTestStream()
    {
        if (IsTestStreamRunning)
        {
            _testGenerator.Stop();
            IsTestStreamRunning = false;
        }
        else
        {
            SelectedMode = ReceiverMode.ExternalHardware;
            _testGenerator.Start();
            IsTestStreamRunning = true;
        }
        UpdateActiveSourceHeader();
        RefreshAllTelemetryViews();
    }

    private void ToggleSdrProcess()
    {
        if (IsSdrRunning)
        {
            _sdrProcess.Stop();
            return;
        }

        SdrConsoleLines.Clear();
        _sdrProcess.StartTrisatSdr(
            repoRoot: RepoRootPath,
            pythonExecutable: PythonExecutable,
            band1: SdrBand1,
            band2: SdrBand2,
            singleSdr: SdrSingleMode,
            udpPort: 5005,
            tcpPort: TcpPort,
            webPort: SdrWebPort,
            emitLegacySimulation: SdrEmitLegacySimulation);
    }

    private void ToggleSbfReplay()
    {
        if (IsSbfReplayRunning)
        {
            _sbfProcess.Stop();
            return;
        }

        _sbfProcess.StartSbfReplay(
            repoRoot: RepoRootPath,
            sbfFilePath: SbfFilePath,
            port: SbfReplayPort,
            merkleTreeXmlPath: string.IsNullOrWhiteSpace(MerkleTreeXmlPath) ? null : MerkleTreeXmlPath,
            publicKeyXmlPath: string.IsNullOrWhiteSpace(PublicKeyXmlPath) ? null : PublicKeyXmlPath,
            osnmaLibPath: string.IsNullOrWhiteSpace(OsnmaLibPath) ? null : OsnmaLibPath,
            pythonExecutable: PythonExecutable);
    }

    private async Task BrowseRepoRootAsync()
    {
        if (_desktopHost is null) return;
        var folder = await _desktopHost.PickFolderAsync("Select TRISAT Repository Root Folder");
        if (!string.IsNullOrWhiteSpace(folder))
            RepoRootPath = folder;
    }

    private async Task<string?> PickFileAsync(string title, string[] patterns)
    {
        if (_desktopHost is null) return null;
        return await _desktopHost.PickOpenFileAsync(title, patterns);
    }

    private async Task ExportNmeaLogAsync()
    {
        if (_desktopHost is null) return;
        var path = await _desktopHost.PickSaveFileAsync(
            "Export NMEA Telemetry Log",
            $"trisat_capture_{DateTime.Now:yyyyMMdd_HHmmss}.nmea",
            ["*.nmea", "*.log", "*.txt"]);

        if (!string.IsNullOrWhiteSpace(path))
        {
            await File.WriteAllLinesAsync(path, NmeaLines);
            AppendSystemLog($"[LOG] Exported {NmeaLines.Count} lines to {path}");
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = url,
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
        }
        catch
        {
            // Ignore shell open errors
        }
    }

    // =========================================================================
    // Ring-Buffered UI Logs
    // =========================================================================

    private void AppendNmeaLine(string line)
    {
        if (PauseNmeaLog) return;
        NmeaLines.Add(line);
        while (NmeaLines.Count > 200)
            NmeaLines.RemoveAt(0);
        ExportNmeaCommand.RaiseCanExecuteChanged();
    }

    private void AddInavMessage(GalileoINavMessage msg)
    {
        InavMessages.Insert(0, msg);
        while (InavMessages.Count > 150)
            InavMessages.RemoveAt(InavMessages.Count - 1);

        if (msg.IsSimulated)
            return;

        ParsedGalileoINavPage? parsed = null;
        if (msg.Data.Length >= 32)
            parsed = GalileoINavParser.TryParseUbxSfrbxPayload(msg.Svid, msg.Data);
        else if (msg.Data.Length == 30)
            parsed = GalileoINavParser.TryParseFull240BitPage(msg.Svid, msg.Data);

        GstTime epochGst = parsed?.DecodedGst
            ?? (msg.GstWeek.HasValue && msg.GstTow.HasValue
                ? new GstTime(msg.GstWeek.Value, msg.GstTow.Value)
                : _gstClock.GetCurrentNetworkGst());

        if (parsed is not null)
            _liveOsnma.IngestPage(parsed, epochGst);

        string? osnma40Hex = parsed is { CrcValid: true, HasNonZeroOsnma: true }
            ? parsed.Osnma40Hex
            : (msg.HasOsnmaField ? msg.OsnmaHkrootHex + msg.OsnmaMackHex : null);

        if (!string.IsNullOrEmpty(osnma40Hex) && osnma40Hex.Length == 10)
        {
            if (_assistServer.IsRunning)
                _assistStore.RecordReferencePage(msg.Svid, epochGst, osnma40Hex);

            if (IsAssistClientConnected)
            {
                var targetUrl = AssistClientUrl;
                var svid = msg.Svid;
                var gstSec = epochGst.TotalSeconds;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var bw = await _assistClient.CheckBitwiseAsync(targetUrl, svid, gstSec, osnma40Hex).ConfigureAwait(false);
                        if (bw is null) return;
                        _liveOsnma.RecordBitwiseResult(bw);
                        RunOnUi(() =>
                        {
                            _bitwiseTotalChecked++;
                            if (!bw.HasReference) _bitwiseNoRef++;
                            else if (bw.Matched) _bitwiseMatched++;
                            else
                            {
                                _bitwiseMismatched++;
                                AppendSystemLog($"[OSNMA BITWISE ALERT] {bw.Reason}");
                            }
                            _lastBitwiseResult = bw;
                            RefreshThreats();
                        });
                    }
                    catch
                    {
                        // Non-fatal network poll miss
                    }
                });
            }
        }
    }

    private void AppendSystemLog(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        SystemLogs.Insert(0, stamped);
        while (SystemLogs.Count > 150)
            SystemLogs.RemoveAt(SystemLogs.Count - 1);
    }

    private void AppendSdrConsole(string line)
    {
        SdrConsoleLines.Add(line);
        while (SdrConsoleLines.Count > 300)
            SdrConsoleLines.RemoveAt(0);
    }

    private void RunOnUi(Action action)
    {
        if (!_useUiDispatcher || Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    public void Dispose()
    {
        _assistServer.Dispose();
        _assistClient.Dispose();
        _testGenerator.Dispose();
        _udpService.Dispose();
        _tcpService.Dispose();
        _serialService.Dispose();
        _bleService.Dispose();
        _locationService.Dispose();
        _sdrProcess.Dispose();
        _sbfProcess.Dispose();
    }
}
