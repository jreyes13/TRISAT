using Trisat.Core.Engines;
using Trisat.Core.Models;
using Xunit;

namespace Trisat.Core.Tests;

public sealed class OsnmaEngineTests
{
    [Fact]
    public void RunAnnexASelfTest_OfficialEuspaVector_AllSixStagesPass()
    {
        var engine = new OsnmaEngine();
        var result = engine.RunAnnexASelfTest();

        Assert.True(result.DsmPkrPaddingPassed);
        Assert.True(result.EcdsaP256KrootPassed);
        Assert.True(result.DsmKrootPaddingPassed);
        Assert.True(result.TeslaChainPassed);
        Assert.True(result.MacSeqPassed);
        Assert.True(result.Adkd4Tag2Passed);
        Assert.True(result.Passed);
    }

    [Fact]
    public void GetReceptionStatus_PreservesHonestUnverifiedProvenance()
    {
        var engine = new OsnmaEngine();

        Assert.Equal(OsnmaReceptionStatus.NotAvailable, engine.GetReceptionStatus(Array.Empty<GalileoINavMessage>()));

        var msg = new GalileoINavMessage(Guid.NewGuid(), 2, 1, [0x01, 0x02, 0x03], 0, DateTimeOffset.UtcNow);
        Assert.Equal(OsnmaReceptionStatus.Unverified, engine.GetReceptionStatus([msg]));
    }
}
