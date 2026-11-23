using Trisat.Core.Models;
using Trisat.Linux.Services;
using Trisat.Linux.ViewModels;
using Xunit;

namespace Trisat.Linux.Tests;

public sealed class LinuxServicesTests
{
    [Fact]
    public void ClassifyPort_IdentifiesUbloxZedF9pByVid()
    {
        var info = LinuxSerialService.ClassifyPort(
            "/dev/ttyACM0",
            vendorId: "1546",
            productId: "01a9",
            manufacturer: "u-blox AG",
            product: "u-blox GNSS receiver",
            byIdName: "usb-u-blox_AG_-_www.u-blox.com_u-blox_GNSS_receiver-if00");

        Assert.True(info.IsKnownGnssDevice);
        Assert.Equal(115200, info.RecommendedBaudRate);
        Assert.Equal("1546:01a9", info.UsbVidPid);
        Assert.Contains("[u-blox ZED-F9P]", info.DisplayName);
    }

    [Fact]
    public void ClassifyPort_IdentifiesGeneqSxBlueAt19200Baud()
    {
        var info = LinuxSerialService.ClassifyPort(
            "/dev/ttyUSB0",
            vendorId: "0403",
            productId: "b5a0",
            manufacturer: "Geneq",
            product: "SXblue GPS",
            byIdName: null);

        Assert.True(info.IsKnownGnssDevice);
        Assert.Equal(19200, info.RecommendedBaudRate);
        Assert.Contains("[FTDI / SXblue]", info.DisplayName);
    }

    [Fact]
    public void ClassifyPort_IdentifiesEsp32Cp210xAt115200Baud()
    {
        var info = LinuxSerialService.ClassifyPort(
            "/dev/ttyUSB1",
            vendorId: "10c4",
            productId: "ea60",
            manufacturer: "Silicon Labs",
            product: "CP2102 USB to UART Bridge Controller",
            byIdName: null);

        Assert.True(info.IsKnownGnssDevice);
        Assert.Equal(115200, info.RecommendedBaudRate);
        Assert.Contains("[ESP32 UART]", info.DisplayName);
    }

    [Fact]
    public void ClassifyPort_TagsRfcommPorts()
    {
        var info = LinuxSerialService.ClassifyPort("/dev/rfcomm0", null, null, null, null, null);
        Assert.False(info.IsKnownGnssDevice);
        Assert.Equal(115200, info.RecommendedBaudRate);
        Assert.Contains("[Bluetooth RFCOMM]", info.DisplayName);
    }

    [Fact]
    public void TryParseGpsdTpv_ParsesValid3DFix()
    {
        const string tpv = """{"class":"TPV","device":"/dev/ttyACM0","mode":3,"lat":41.637800,"lon":-3.868900,"altMSL":837.5,"epx":1.2,"epy":1.6}""";

        var pos = LinuxLocationService.TryParseGpsdTpv(tpv);
        Assert.NotNull(pos);
        Assert.Equal(41.6378, pos.Latitude, 4);
        Assert.Equal(-3.8689, pos.Longitude, 4);
        Assert.Equal(837.5, pos.AltitudeMeters, 1);
        Assert.Equal(2.0, pos.HorizontalAccuracyMeters!.Value, 2);
        Assert.Equal("Linux gpsd (3D Fix)", pos.FixDescription);
        Assert.False(pos.IsSimulated);
    }

    [Fact]
    public void TryParseGpsdTpv_IgnoresNoFixAndNonTpvMessages()
    {
        Assert.Null(LinuxLocationService.TryParseGpsdTpv("""{"class":"TPV","mode":1,"lat":41.6,"lon":-3.8}"""));
        Assert.Null(LinuxLocationService.TryParseGpsdTpv("""{"class":"SKY","satellites":[]}"""));
    }

    [Fact]
    public void BleLineFramer_ReassemblesSplitNordicUartLines()
    {
        var framer = new BleLineFramer();

        var first = framer.Feed("$GNGGA,123519,4138.268,N,", isSinglePacketJson: false);
        Assert.Empty(first);

        var second = framer.Feed("00352.134,W,1,08,0.9,837.0,M,46.9,M,,*47\r\n$GAGSV,", isSinglePacketJson: false);
        Assert.Single(second);
        Assert.StartsWith("$GNGGA,", second[0]);

        var third = framer.Feed("1,1,01,02,45,120,42*7A\n", isSinglePacketJson: false);
        Assert.Single(third);
        Assert.StartsWith("$GAGSV,", third[0]);
    }

    [Fact]
    public void BleLineFramer_PassesSinglePacketTrisatJsonDirectly()
    {
        var framer = new BleLineFramer();
        var lines = framer.Feed("{\"type\":\"location\",\"lat\":41.6378,\"lon\":-3.8689}", isSinglePacketJson: true);
        Assert.Single(lines);
        Assert.StartsWith("{\"type\":\"location\"", lines[0]);
    }

    [Fact]
    public void MainViewModel_IngestsNmeaAndRunsAnnexASelfTestOnLinux()
    {
        using var vm = new MainViewModel(desktopHost: null, autoStartNetwork: false, useUiDispatcher: false);

        Assert.Contains("python3", vm.PythonExecutable);

        vm.IngestTelemetryLine("$GNGGA,123519,4138.268,N,00352.134,W,1,08,0.9,837.0,M,46.9,M,,*47", "UnitTest");
        vm.IngestTelemetryLine("$GAGSV,1,1,02,02,52,135,44,11,38,240,41*7B", "UnitTest");

        Assert.NotNull(vm.EffectivePosition);
        Assert.Equal(2, vm.VisibleSatelliteCount);
        Assert.Equal(2, vm.GalileoCount);

        vm.RunAnnexASelfTestCommand.Execute(null);
        Assert.NotNull(vm.AnnexASelfTestResult);
        Assert.True(vm.AnnexASelfTestResult.Passed);

        vm.SelectedMode = ReceiverMode.TleSimulation;
        Assert.True(vm.IsSimulationMode);
    }
}
