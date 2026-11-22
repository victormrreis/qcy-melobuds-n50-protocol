using N50.Core.Protocol;
using Xunit;

namespace N50.Core.Tests;

public sealed class ProtocolGuardTests
{
    [Theory]
    [InlineData(0x0E)] // FUNCTION
    [InlineData(0xC5)] // ADV_SYSTEM_OPERATION (factory reset / clear pairing)
    [InlineData(0xE7)] // REBOOT
    [InlineData(0xD4)] // OTA
    [InlineData(0xE1)] // OTA
    [InlineData(0xE5)] // OTA
    [InlineData(0xAA)] // Unmapped opcode
    public void BlocksForbiddenAndUnmappedOpcodes(byte opCode)
    {
        byte[] bad = RcspFramer.BuildCommand(opCode, 0x01, [0x00]);
        Assert.Throws<ProtocolGuardException>(() => ProtocolGuard.ValidateOutboundFrame(bad));
    }

    [Theory]
    [InlineData(0x01)] // DEVICE_NAME rename
    [InlineData(0x03)] // LED
    [InlineData(0x04)] // MIC
    [InlineData(0x06)] // PRODUCT
    [InlineData(0x09)] // LANGUAGE
    [InlineData(0x7F)] // Unmapped
    public void BlocksForbiddenAdvSetAttributes(byte attr)
    {
        byte[] ltv = JieLiLtv.Encode(attr, [0x01]);
        byte[] bad = RcspFramer.BuildCommand(RcspOpCode.AdvSet, 0x01, ltv);
        Assert.Throws<ProtocolGuardException>(() => ProtocolGuard.ValidateOutboundFrame(bad));
    }

    [Theory]
    [InlineData(0x01)] // RESET_DEFAULT
    [InlineData(0x02)] // CLEAR_PAIR
    [InlineData(0x03)] // FACTORY_RESET
    [InlineData(0x0D)] // TEST_MODE
    [InlineData(0x18)] // PAIR_NAME
    [InlineData(0x1E)] // TAKE_PHOTO
    [InlineData(0x1F)] // STANDBY
    [InlineData(0x25)] // RESTART
    [InlineData(0x26)] // RESET_KEYS
    [InlineData(0x34)] // TWS_ENABLE
    [InlineData(0x3E)] // SYNC_TIME
    [InlineData(0x3F)] // ALARM
    public void BlocksForbiddenCustomItemsForReadAndWrite(byte itemId)
    {
        byte[] badWrite = RcspFramer.BuildCommand(RcspOpCode.Custom, 0x01, QcyTlv.Encode(itemId, [0x01]));
        byte[] badRead = RcspFramer.BuildCommand(RcspOpCode.Custom, 0x01, QcyTlv.Encode(0xFE, [itemId]));

        Assert.Throws<ProtocolGuardException>(() => ProtocolGuard.ValidateOutboundFrame(badWrite));
        Assert.Throws<ProtocolGuardException>(() => ProtocolGuard.ValidateOutboundFrame(badRead));
    }

    [Fact]
    public void BlocksAutoOffZeroMinutes()
    {
        Assert.Throws<ProtocolGuardException>(() => N50Commands.BuildSetAutoOffCustom(0x01, 0));

        byte[] rawZero = RcspFramer.BuildCommand(RcspOpCode.Custom, 0x01, QcyTlv.Encode(0x14, [0x00, 0x00, 0x00, 0x00]));
        Assert.Throws<ProtocolGuardException>(() => ProtocolGuard.ValidateOutboundFrame(rawZero));
    }

    [Fact]
    public void BlocksSetSysInfoOtherThanPublicEq()
    {
        byte[] wrongFunc = RcspFramer.BuildCommand(RcspOpCode.SetSysInfo, 0x01, [0x01, 0x0C, 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Throws<ProtocolGuardException>(() => ProtocolGuard.ValidateOutboundFrame(wrongFunc));
    }
}
