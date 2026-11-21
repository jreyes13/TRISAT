using Trisat.Core.Models;
using Trisat.Core.Parsers;
using Xunit;

namespace Trisat.Core.Tests;

public sealed class NmeaAndJsonParserTests
{
    [Fact]
    public void FormatAndVerifyChecksum_ValidSentence_Passes()
    {
        var sentence = NmeaParser.FormatSentence("GNGGA,123519.00,4138.2680,N,00352.1340,W,2,11,0.85,837.0,M,48.0,M,2.5,0123");
        Assert.True(NmeaParser.VerifyChecksum(sentence));
        Assert.False(NmeaParser.VerifyChecksum(sentence[..^2] + "00"));
    }

    [Fact]
    public void TryParseGga_ParsesCoordinatesFixAndDgpsFields()
    {
        var line = "$GNGGA,123519.00,4138.2680,N,00352.1340,W,2,11,0.85,837.0,M,48.0,M,2.5,0123*00";
        var pos = NmeaParser.TryParseGga(line);

        Assert.NotNull(pos);
        Assert.InRange(pos.Latitude, 41.637, 41.639);
        Assert.InRange(pos.Longitude, -3.870, -3.868);
        Assert.Equal(837.0, pos.AltitudeMeters, 1);
        Assert.Equal(11, pos.SatellitesInFix);
        Assert.Equal("0.85", pos.Hdop);
        Assert.Equal("2.5s", pos.DgpsAge);
        Assert.Equal("0123", pos.DgpsStationId);
        Assert.False(pos.IsSimulated);
    }

    [Fact]
    public void TryParseGsv_ParsesGalileoAndGpsSatellites()
    {
        var line = "$GAGSV,2,1,06,02,62,045,44,11,48,132,41,19,35,220,39,24,74,310,47*70";
        var sats = NmeaParser.TryParseGsv(line);

        Assert.Equal(4, sats.Count);
        Assert.All(sats, s => Assert.Equal(GnssConstellation.Galileo, s.Constellation));
        Assert.Equal("E02", sats[0].FormattedLabel);
        Assert.Equal(62, sats[0].Elevation);
        Assert.Equal(45, sats[0].Azimuth);
        Assert.Equal(44, sats[0].Cno);
    }

    [Fact]
    public void TryParseNavigationSentence_ParsesGalinavWithOsnmaSlice()
    {
        var line = "$GALINAV,2,16,048210492204E060610BDF26D77B5BF8C9CBFCF70422081475FD445DF0FF,827BB238C8*00";
        var msg = NmeaParser.TryParseNavigationSentence(line);

        Assert.NotNull(msg);
        Assert.Equal(2, msg.Svid);
        Assert.Equal(16, msg.SubmessageId);
        Assert.Equal(30, msg.Data.Length);
        Assert.Equal("82", msg.OsnmaHkrootHex);
        Assert.Equal("7BB238C8", msg.OsnmaMackHex);
        Assert.Equal("UNVERIFIED RX", msg.ProvenanceBadge);
    }

    [Fact]
    public void TrisatJsonParser_PreservesSimulatedProvenance()
    {
        const string locJson = """{"type":"location","source":"simulated","mode":"E1+E6 HAS-UA","lat":41.6378,"lon":-3.8689,"alt":837.0,"hdop":0.9,"pdop":1.4}""";
        var parsedLoc = TrisatJsonParser.TryParse(locJson);

        Assert.NotNull(parsedLoc);
        Assert.True(parsedLoc.IsSimulated);
        Assert.NotNull(parsedLoc.Position);
        Assert.True(parsedLoc.Position.IsSimulated);
        Assert.Equal(41.6378, parsedLoc.Position.Latitude, 4);
        Assert.Equal(0.9, parsedLoc.Dop?.Hdop);

        const string satJson = """{"type":"sat_status","source":"simulated","satellites":[{"svid":11,"const":"GALILEO","band":"GAL_E1","elev":58.5,"azim":124.0,"cno":46,"used":true,"osnma":true,"hasPpp":true}]}""";
        var parsedSats = TrisatJsonParser.TryParse(satJson);

        Assert.NotNull(parsedSats?.Satellites);
        Assert.Single(parsedSats.Satellites);
        Assert.Equal("E11", parsedSats.Satellites[0].FormattedLabel);
        Assert.True(parsedSats.Satellites[0].HasHasPpp);
    }
}
