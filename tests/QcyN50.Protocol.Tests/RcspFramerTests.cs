using N50.Core.Protocol;
using Xunit;

namespace N50.Core.Tests;

public sealed class RcspFramerTests
{
    [Fact]
    public void ReassemblesFragmentedAndConcatenatedFrames()
    {
        byte[] f1 = Convert.FromHexString("FEDCBA00C1000700010400504BC8EF");
        byte[] f2 = Convert.FromHexString("FEDCBA00FF000700021703010302EF");
        byte[] stream = [0x99, 0xAA, .. f1, .. f2]; // ruído inicial + 2 frames colados

        var framer = new RcspFramer();
        var collected = new List<RcspFrame>();

        // Alimenta em pedaços de 3 bytes para simular fragmentação BLE
        for (int i = 0; i < stream.Length; i += 3)
        {
            int len = Math.Min(3, stream.Length - i);
            collected.AddRange(framer.Feed(stream.AsSpan(i, len)));
        }

        Assert.Equal(2, collected.Count);

        Assert.True(collected[0].IsSuccessResponse);
        Assert.Equal(RcspOpCode.AdvGet, collected[0].OpCode);
        Assert.Equal((byte)0x01, collected[0].SequenceNumber);
        Assert.Equal("0400504BC8", Convert.ToHexString(collected[0].ParamData));

        Assert.True(collected[1].IsSuccessResponse);
        Assert.Equal(RcspOpCode.Custom, collected[1].OpCode);
        Assert.Equal((byte)0x02, collected[1].SequenceNumber);
        Assert.Equal("1703010302", Convert.ToHexString(collected[1].ParamData));
    }

    [Fact]
    public void BuildsExpectedAckFrame()
    {
        byte[] ack = N50Commands.BuildAck(RcspOpCode.AdvRequestOperation, 0x07);
        Assert.Equal("FEDCBA00C400020007EF", Convert.ToHexString(ack));
    }
}
