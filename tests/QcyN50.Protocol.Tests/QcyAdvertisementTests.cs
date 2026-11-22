using N50.Core.Protocol;
using Xunit;

namespace N50.Core.Tests;

public sealed class QcyAdvertisementTests
{
    [Fact]
    public void ParsesPairFreeAdvertVectorExactly()
    {
        // Vetor de teste de PairFree/data/qcy/test_vectors.json (campo "advert")
        byte[] raw = Convert.FromHexString("5d45000500bc602b000000bbaaccffeedd00221133665544");

        Assert.True(QcyAdvertisementParser.TryParse(raw, out var adv));
        Assert.NotNull(adv);
        Assert.Equal((ushort)23877, adv!.VendorId); // 0x5D45
        Assert.Equal("AA:BB:CC:DD:EE:FF", adv.ClassicMac);
        Assert.Equal("11:22:33:44:55:66", adv.OtherMac);
        Assert.Equal(60, adv.Left.Percent);
        Assert.True(adv.Left.IsCharging);           // 0xBC = 0x80 | 60
        Assert.Equal(96, adv.Right.Percent);
        Assert.False(adv.Right.IsCharging);         // 0x60 = 96
        Assert.Equal(43, adv.Case.Percent);
        Assert.False(adv.Case.IsCharging);          // 0x2B = 43
        Assert.Equal((byte)2, adv.ColorIndex);      // (0x05 & 0x0E) >> 1 = 2 (APK Devicebind)
        Assert.False(adv.IsNoBattery);
    }

    [Theory]
    [InlineData(0x477A, true, false, true)]
    [InlineData(0x477C, true, false, true)]
    [InlineData(0x8070, true, true, false)]
    [InlineData(0x5D45, false, false, false)]
    public void ClassifiesN50AndJieLiVendorIds(ushort vendorId, bool isJieLi, bool isNewJieLi, bool isN50)
    {
        byte[] payload = new byte[24];
        payload[0] = (byte)(vendorId >> 8);
        payload[1] = (byte)(vendorId & 0xFF);
        payload[5] = 80;

        Assert.True(QcyAdvertisementParser.TryParse(payload, out var adv));
        Assert.Equal(isJieLi, adv!.IsJieLi);
        Assert.Equal(isNewJieLi, adv.IsNewJieLi);
        Assert.Equal(isN50, adv.IsN50);
    }

    [Fact]
    public void RejectsShortPayloadAndMarksZeroBatteryAsNoBattery()
    {
        Assert.False(QcyAdvertisementParser.TryParse(new byte[19], out _));

        byte[] emptyBats = new byte[20];
        emptyBats[0] = 0x47;
        emptyBats[1] = 0x7A;
        emptyBats[5] = 0x80; // 0% com bit charging -> inválido no APK (percent = 0)
        emptyBats[6] = 0x65; // 101% -> inválido no APK
        Assert.True(QcyAdvertisementParser.TryParse(emptyBats, out var adv));
        Assert.True(adv!.IsNoBattery);
        Assert.Null(adv.Left.Percent);
        Assert.False(adv.Left.IsCharging);
        Assert.Null(adv.Right.Percent);
    }
}
