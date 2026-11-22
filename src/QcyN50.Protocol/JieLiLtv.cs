namespace N50.Core.Protocol;

public sealed record JieLiLtvItem(byte Type, byte[] Value);

/// <summary>
/// Codificação LTV do SDK JieLi: [len = 1 + value.Length][type][value...].
/// Atenção: o byte len inclui o byte type (ParseDataUtil.packLTVPacket no APK).
/// </summary>
public static class JieLiLtv
{
    public static byte[] Encode(byte type, ReadOnlySpan<byte> value)
    {
        if (value.Length > 254)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Valor LTV JieLi excede 254 bytes.");
        }

        byte[] result = new byte[2 + value.Length];
        result[0] = (byte)(1 + value.Length);
        result[1] = type;
        value.CopyTo(result.AsSpan(2));
        return result;
    }

    public static List<JieLiLtvItem> DecodeAll(ReadOnlySpan<byte> data)
    {
        var items = new List<JieLiLtvItem>();
        int offset = 0;

        while (offset < data.Length)
        {
            byte len = data[offset];
            if (len < 1 || offset + 1 + len > data.Length)
            {
                break;
            }

            byte type = data[offset + 1];
            byte[] value = len > 1 ? data.Slice(offset + 2, len - 1).ToArray() : [];
            items.Add(new JieLiLtvItem(type, value));
            offset += 1 + len;
        }

        return items;
    }
}
