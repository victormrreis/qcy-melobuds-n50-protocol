using System.Buffers.Binary;

namespace N50.Core.Protocol;

public static class RcspOpCode
{
    public const byte GetTargetInfo = 0x03;
    public const byte GetSysInfo = 0x07;
    public const byte SetSysInfo = 0x08;
    public const byte SysInfoAutoUpdate = 0x09;
    public const byte AdvSet = 0xC0;
    public const byte AdvGet = 0xC1;
    public const byte AdvNotify = 0xC2;
    public const byte AdvRequestOperation = 0xC4;
    public const byte Custom = 0xFF;

    // Proibidos (referenciados apenas para bloqueio explícito no ProtocolGuard)
    public const byte ForbiddenFunction = 0x0E;
    public const byte ForbiddenAdvSystemOp = 0xC5;
    public const byte ForbiddenReboot = 0xE7;
}

public sealed record RcspFrame(
    byte Flags,
    byte OpCode,
    byte SequenceNumber,
    byte? Status,
    byte[] ParamData,
    byte[] RawBytes)
{
    public bool IsCommand => (Flags & 0x80) != 0;
    public bool ExpectsResponse => (Flags & 0x40) != 0;
    public bool IsResponse => !IsCommand;
    public bool IsSuccessResponse => IsResponse && Status == 0x00;
}

public sealed class RcspFramer
{
    public const byte FlagCommandWithResponse = 0xC0;
    public const byte FlagCommandNoResponse = 0x80;
    public const byte FlagResponse = 0x00;

    private static readonly byte[] Header = [0xFE, 0xDC, 0xBA];
    private const byte Tail = 0xEF;
    private const int MinFrameLength = 9; // FE DC BA | flags | op | len(2) | sn | EF
    private const int MaxParamLength = 2048;

    private readonly List<byte> _buffer = new(256);

    public static byte[] BuildCommand(byte opCode, byte sn, ReadOnlySpan<byte> paramData, bool expectsResponse = true)
    {
        int paramLen = 1 + paramData.Length;
        byte[] frame = new byte[8 + paramLen];
        frame[0] = 0xFE;
        frame[1] = 0xDC;
        frame[2] = 0xBA;
        frame[3] = expectsResponse ? FlagCommandWithResponse : FlagCommandNoResponse;
        frame[4] = opCode;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5, 2), (ushort)paramLen);
        frame[7] = sn;
        paramData.CopyTo(frame.AsSpan(8));
        frame[^1] = Tail;
        return frame;
    }

    public static byte[] BuildAckResponse(byte opCode, byte sn, byte status = 0x00)
    {
        return
        [
            0xFE, 0xDC, 0xBA,
            FlagResponse,
            opCode,
            0x00, 0x02,
            status,
            sn,
            Tail
        ];
    }

    public static bool TryParseSingle(ReadOnlySpan<byte> data, out RcspFrame? frame)
    {
        frame = null;
        if (data.Length < MinFrameLength)
        {
            return false;
        }

        if (data[0] != 0xFE || data[1] != 0xDC || data[2] != 0xBA)
        {
            return false;
        }

        byte flags = data[3];
        byte opCode = data[4];
        ushort paramLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(5, 2));
        int totalLen = 8 + paramLen; // 7 header bytes + paramLen + 1 tail byte
        if (paramLen == 0 || paramLen > MaxParamLength || data.Length < totalLen)
        {
            return false;
        }

        if (data[totalLen - 1] != Tail)
        {
            return false;
        }

        bool isCommand = (flags & 0x80) != 0;
        if (isCommand)
        {
            byte sn = data[7];
            byte[] paramData = paramLen > 1 ? data.Slice(8, paramLen - 1).ToArray() : [];
            frame = new RcspFrame(flags, opCode, sn, null, paramData, data[..totalLen].ToArray());
            return true;
        }
        else
        {
            if (paramLen < 2)
            {
                return false;
            }

            byte status = data[7];
            byte sn = data[8];
            byte[] paramData = paramLen > 2 ? data.Slice(9, paramLen - 2).ToArray() : [];
            frame = new RcspFrame(flags, opCode, sn, status, paramData, data[..totalLen].ToArray());
            return true;
        }
    }

    /// <summary>
    /// Acumula fragmentos BLE e retorna todos os frames RCSP completos presentes no stream.
    /// </summary>
    public List<RcspFrame> Feed(ReadOnlySpan<byte> chunk)
    {
        var results = new List<RcspFrame>();
        foreach (byte b in chunk)
        {
            _buffer.Add(b);
        }

        while (_buffer.Count >= MinFrameLength)
        {
            int start = FindHeader(_buffer);
            if (start < 0)
            {
                // Mantém no máximo 2 bytes finais caso façam parte de um FE DC parcial
                if (_buffer.Count > 2)
                {
                    _buffer.RemoveRange(0, _buffer.Count - 2);
                }
                break;
            }

            if (start > 0)
            {
                _buffer.RemoveRange(0, start);
            }

            if (_buffer.Count < 7)
            {
                break;
            }

            ushort paramLen = (ushort)((_buffer[5] << 8) | _buffer[6]);
            if (paramLen == 0 || paramLen > MaxParamLength)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            int totalLen = 8 + paramLen;
            if (_buffer.Count < totalLen)
            {
                break; // Aguardando restante do frame fragmentado
            }

            byte[] candidate = _buffer.GetRange(0, totalLen).ToArray();
            if (TryParseSingle(candidate, out var frame) && frame is not null)
            {
                results.Add(frame);
                _buffer.RemoveRange(0, totalLen);
            }
            else
            {
                // Falso sincronismo em FE DC BA; descarta 1 byte e procura o próximo
                _buffer.RemoveAt(0);
            }
        }

        return results;
    }

    public void Clear() => _buffer.Clear();

    private static int FindHeader(List<byte> buf)
    {
        for (int i = 0; i <= buf.Count - Header.Length; i++)
        {
            if (buf[i] == Header[0] && buf[i + 1] == Header[1] && buf[i + 2] == Header[2])
            {
                return i;
            }
        }
        return -1;
    }
}
