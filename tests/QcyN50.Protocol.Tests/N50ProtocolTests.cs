using N50.Core.Protocol;
using Xunit;

namespace N50.Core.Tests;

public sealed class N50ProtocolTests
{
    [Fact]
    public void BuildsDocumentedCommandFramesExactly()
    {
        Assert.Equal("FEDCBAC0030006010000002000EF",
            Convert.ToHexString(N50Commands.BuildGetTargetInfo(1, N50Commands.TargetMaskFirmware)));

        Assert.Equal("FEDCBAC0C100050100000001EF",
            Convert.ToHexString(N50Commands.BuildAdvGet(1, N50Commands.AdvMaskBattery)));

        Assert.Equal("FEDCBAC0C100050100000D21EF",
            Convert.ToHexString(N50Commands.BuildAdvGet(1, N50Commands.AdvMaskAppBatteryBatch)));

        Assert.Equal("FEDCBAC0C0000401020502EF",
            Convert.ToHexString(N50Commands.BuildSetGameModeAdv(1, true)));

        Assert.Equal("FEDCBAC0C0000401020501EF",
            Convert.ToHexString(N50Commands.BuildSetGameModeAdv(1, false)));

        Assert.Equal("FEDCBAC007000601FF00000010EF",
            Convert.ToHexString(N50Commands.BuildGetGraphicEq(1)));

        Assert.Equal("FEDCBAC0FF000401FE0117EF",
            Convert.ToHexString(N50Commands.BuildCustomRead(1, 0x17)));

        Assert.Equal("FEDCBAC0FF0006011703020000EF",
            Convert.ToHexString(N50Commands.BuildSetAncCustom(1, 0x02, 0x00, 0x00)));

        // Regra especial APK: Transparência (3, 1, 0) é remapeada para (3, 2, 0) no envio
        Assert.Equal("FEDCBAC0FF0006011703030200EF",
            Convert.ToHexString(N50Commands.BuildSetAncCustom(1, 0x03, 0x01, 0x00)));

        Assert.Equal("FEDCBAC0FF00070114041E000000EF",
            Convert.ToHexString(N50Commands.BuildSetAutoOffCustom(1, 30)));
    }

    [Fact]
    public void ParsesFirmwareTargetInfoNibbles()
    {
        byte[] param = Convert.FromHexString("03051234");
        var info = N50Parsers.ParseTargetInfo(param);
        Assert.Equal("V_1.2.3.4", info.FirmwareVersion);
    }

    [Theory]
    [InlineData("ff051703010302", 0x17, "010302")]
    [InlineData("ff03090102", 0x09, "02")]
    [InlineData("ff03100100", 0x10, "00")]
    [InlineData("ff03240101", 0x24, "01")]
    [InlineData("ff041d02040f", 0x1D, "040F")]
    [InlineData("ff061404ffff0000", 0x14, "FFFF0000")]
    [InlineData("ff052c03000101", 0x2C, "000101")]
    public void ParsesOpenQcyAndPairFreeN70ReplyVectors(string a001Hex, byte expectedItem, string expectedValueHex)
    {
        // Vetores reais de hardware N70 (OpenQCY-Desktop MIT / PairFree test_vectors.json)
        ReadOnlySpan<byte> innerTlv = QcyTlv.UnwrapA001Envelope(Convert.FromHexString(a001Hex));
        var items = QcyTlv.DecodeAll(innerTlv);

        Assert.Single(items);
        Assert.Equal(expectedItem, items[0].Id);
        Assert.Equal(expectedValueHex, Convert.ToHexString(items[0].Value));

        if (expectedItem == 0x17)
        {
            var anc = N50Parsers.ParseAncItem(items[0].Value);
            Assert.Equal(new N50AncState(0x01, 0x03, 0x02), anc);
        }
        else if (expectedItem == 0x1D)
        {
            var pv = N50Parsers.ParsePromptVolumeItem(items[0].Value);
            Assert.Equal(new N50PromptVolumeState(4, 15), pv);
        }
        else if (expectedItem == 0x14)
        {
            var ao = N50Parsers.ParseAutoOffItem(items[0].Value);
            Assert.True(ao!.IsDisabled);
        }
    }

    [Fact]
    public void ParsesAdvBatteryAndTouchMappings()
    {
        // LTV bateria (00: L=80% chg, R=75%, Case=60%) + WorkMode=2 (Game) + Touch (R 2x -> Próxima)
        byte[] param =
        [
            .. JieLiLtv.Encode(0x00, [0x80 | 80, 75, 60]),
            .. JieLiLtv.Encode(0x05, [0x02]),
            .. JieLiLtv.Encode(0x02, [0x02, 0x02, 0x03]),
            .. JieLiLtv.Encode(0x0C, [50])
        ];

        var adv = N50Parsers.ParseAdvInfo(param);
        Assert.Equal(80, adv.Battery!.Left.Percent);
        Assert.True(adv.Battery.Left.IsCharging);
        Assert.Equal(75, adv.Battery.Right.Percent);
        Assert.Equal(60, adv.Battery.Case.Percent);
        Assert.Equal((byte)0x02, adv.WorkMode);
        Assert.Equal((byte)50, adv.Balance);
        Assert.Single(adv.TouchMappings!);
        Assert.Equal("R", adv.TouchMappings![0].SideName);
        Assert.Equal("2x", adv.TouchMappings[0].GestureName);
        Assert.Equal("Próxima", adv.TouchMappings[0].ActionName);
    }

    [Fact]
    public void ParsesGraphicEqAndParametricEqV2()
    {
        // Graphic EQ: func=FF, LTV 0C 04 mode=FF, 10 gains (-4..+5)
        sbyte[] expectedGains = [-4, -2, 0, 1, 2, 3, 2, 0, -1, 5];
        byte[] eqParam = [0xFF, .. JieLiLtv.Encode(0x04, [0xFF, .. expectedGains.Select(g => unchecked((byte)g))])];
        var graphic = N50Parsers.ParseGraphicEq(eqParam);
        Assert.NotNull(graphic);
        Assert.Equal((byte)0xFF, graphic!.Mode);
        Assert.Equal(expectedGains, graphic.GainsDb);

        // Parametric EQ v2 (item 0x22): eqIdx=0, master=-150 (-1.5dB), 1 band (1000Hz, +350=+3.5dB, Q=141=1.41, type=0xFF)
        byte[] peq =
        [
            0x00,
            0x6A, 0xFF, // -150 LE
            0xE8, 0x03, // 1000 Hz LE
            0x5E, 0x01, // +350 LE
            0x8D, 0x00, // 141 LE
            0xFF
        ];
        var parsed = N50Parsers.ParseParametricEqItem(0x22, peq);
        Assert.NotNull(parsed);
        Assert.Equal(-1.5, parsed!.MasterGainDb, 2);
        Assert.Single(parsed.Bands);
        Assert.Equal((ushort)1000, parsed.Bands[0].FrequencyHz);
        Assert.Equal(3.5, parsed.Bands[0].GainDb, 2);
        Assert.Equal(1.41, parsed.Bands[0].QFactor, 2);
        Assert.Equal((byte)0xFF, parsed.Bands[0].FilterType);
    }

    [Fact]
    public void ParsesRealN50HardwareCapturesFromPhaseB()
    {
        // Capturas reais do hardware QCY MeloBuds N50 (firmware V_0.5.2.1, Fase B)
        var fwFrame = ParseSingle("FEDCBA00030006000103050521EF");
        Assert.Equal("V_0.5.2.1", N50Parsers.ParseTargetInfo(fwFrame.ParamData).FirmwareVersion);

        var mtuFrame = ParseSingle("FEDCBA000300080002050D0110021CEF");
        var mtuInfo = N50Parsers.ParseTargetInfo(mtuFrame.ParamData);
        Assert.Equal((ushort)272, mtuInfo.SendMtu);
        Assert.Equal((ushort)540, mtuInfo.RecvMtu);

        var advBatch = ParseSingle("FEDCBA00C1000D0002020A020205010400646400EF");
        var adv = N50Parsers.ParseAdvInfo(advBatch.ParamData);
        Assert.Equal(100, adv.Battery!.Left.Percent);
        Assert.Equal(100, adv.Battery.Right.Percent);
        Assert.Null(adv.Battery.Case.Percent);
        Assert.Equal((byte)1, adv.WorkMode);
        Assert.Equal((byte)2, adv.SleepMode);

        var touchFrame = ParseSingle("FEDCBA00C1004000033D0201010602010501020602020501030402030701040002040001050702050B01818002818001828002828001838002838001848002848001858002850BEF");
        var touches = N50Parsers.ParseAdvInfo(touchFrame.ParamData).TouchMappings!;
        Assert.Equal(20, touches.Count);
        Assert.Contains(touches, t => t.Side == 2 && t.Gesture == 5 && t.ActionJl == 0x0B && t.ActionName == "Alternar ANC");
        Assert.Contains(touches, t => t.Side == 2 && t.Gesture == 0x85 && t.IsSingleEarbudMode && t.ActionJl == 0x0B);

        var peqFrame = ParseSingle("FEDCBA00FF0023000A221F0312FD230000005000003200C800460002E6004AFC6400025014D4FEC80002EF");
        var tlvs = QcyTlv.DecodeAll(peqFrame.ParamData);
        var peq = N50Parsers.ParseParametricEqItem(0x22, tlvs[0].Value)!;
        Assert.Equal((byte)0x03, peq.EqIndex);
        Assert.Equal(-7.5, peq.MasterGainDb, 2);
        Assert.Equal(4, peq.Bands.Count);
        Assert.Equal((ushort)230, peq.Bands[2].FrequencyHz);
        Assert.Equal(-9.5, peq.Bands[2].GainDb, 2);

        // Round-trip do construtor BuildSetParametricEqV2 deve gerar os mesmos bytes TLV
        byte[] rebuilt = N50Commands.BuildSetParametricEqV2(0x0A, peq.EqIndex, peq.MasterGainDb, peq.Bands);
        var rebuiltFrame = ParseSingle(Convert.ToHexString(rebuilt));
        Assert.Equal(Convert.ToHexString(peqFrame.ParamData), Convert.ToHexString(rebuiltFrame.ParamData));

        var pvFrame = ParseSingle("FEDCBA00FF000600091D020808EF");
        var pv = N50Parsers.ParsePromptVolumeItem(QcyTlv.DecodeAll(pvFrame.ParamData)[0].Value)!;
        Assert.Equal((byte)8, pv.Volume);
        Assert.Equal((byte)8, pv.MaxVolume);
    }

    [Fact]
    public void BuildsTenBandParametricEqFromPresetsAndPassesProtocolGuard()
    {
        var heavyBass = N50EqPresets.BuiltIn.First(p => p.Id == "heavy_bass");
        var bands = N50EqPresets.FromTenBandGains(heavyBass.TenBandGainsDb);
        double master = N50EqPresets.ComputeSafeMasterGain(heavyBass.TenBandGainsDb);

        Assert.Equal(10, bands.Count);
        Assert.Equal(-6.0, master, 2);

        byte[] frame = N50Commands.BuildSetParametricEqV2(0x01, heavyBass.EqIndex, master, bands);
        ProtocolGuard.ValidateOutboundFrame(frame);

        var parsed = ParseSingle(Convert.ToHexString(frame));
        var tlvs = QcyTlv.DecodeAll(parsed.ParamData);
        var decoded = N50Parsers.ParseParametricEqItem(0x22, tlvs[0].Value)!;
        Assert.Equal(10, decoded.Bands.Count);
        Assert.Equal(heavyBass.EqIndex, decoded.EqIndex);
        Assert.Equal(-6.0, decoded.MasterGainDb, 2);
        Assert.Equal(6.0, decoded.Bands[0].GainDb, 2);
    }

    private static RcspFrame ParseSingle(string hex)
    {
        Assert.True(RcspFramer.TryParseSingle(Convert.FromHexString(hex), out var f));
        return f!;
    }
}
