using Trisat.Core.Engines;
using Trisat.Core.Models;
using Xunit;

namespace Trisat.Core.Tests;

public sealed class OrbitAndDopTests
{
    private const string SampleGalileoTle = """
        GSAT0211 (GALILEO 15)
        1 41859U 16069A   26100.50000000 -.00000050  00000-0  00000-0 0  9991
        2 41859  55.1200 120.4500 0003200  45.0000 315.1000  1.70475000 58001
        GSAT0101 (GALILEO-PFM)
        1 37846U 11060A   26100.50000000 -.00000040  00000-0  00000-0 0  9992
        2 37846  55.8000 240.1000 0004100  90.0000 180.0000  1.70474000 89001
        """;

    [Fact]
    public void ParseTleAndResolvePrn_MapsOfficialGsatIds()
    {
        var tles = TleService.ParseTleText(SampleGalileoTle, "GALILEO");

        Assert.Equal(2, tles.Count);
        Assert.Equal(2, OrbitPropagator.ResolvePrn(tles[0].Name, GnssConstellation.Galileo, 0));
        Assert.Equal(11, OrbitPropagator.ResolvePrn(tles[1].Name, GnssConstellation.Galileo, 1));
    }

    [Fact]
    public void CalculateDop_WellDistributedGeometry_ProducesReasonableValues()
    {
        var sats = new List<SatelliteInfo>
        {
            new("E02", 2, 75, 0, 46, GnssConstellation.Galileo, true, true),
            new("E11", 11, 30, 0, 42, GnssConstellation.Galileo, true, true),
            new("E19", 19, 30, 120, 41, GnssConstellation.Galileo, true, true),
            new("E24", 24, 30, 240, 43, GnssConstellation.Galileo, true, true),
            new("G03", 3, 45, 60, 44, GnssConstellation.Gps, true, false),
            new("G14", 14, 45, 300, 42, GnssConstellation.Gps, true, false)
        };

        var dop = OrbitPropagator.CalculateDop(sats);

        Assert.NotNull(dop.Hdop);
        Assert.NotNull(dop.Vdop);
        Assert.NotNull(dop.Pdop);
        Assert.NotNull(dop.Gdop);
        Assert.InRange(dop.Hdop!.Value, 0.5, 2.5);
        Assert.InRange(dop.Pdop!.Value, 0.8, 3.5);
        Assert.True(dop.Gdop >= dop.Pdop);
    }
}
