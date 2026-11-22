namespace N50.Core.Protocol;

public sealed record QcyTlvItem(byte Id, byte[] Value);

/// <summary>
/// Codificação TLV dos itens QCY embutidos no opcode RCSP Custom 0xFF:
/// [itemId][valLen][value...] (valLen NÃO inclui itemId; sem prefixo FF &lt;totalLen&gt; do A001).
/// </summary>
public static class QcyTlv
{
    public static byte[] Encode(byte itemId, ReadOnlySpan<byte> value)
    {
        if (value.Length > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Valor TLV QCY excede 255 bytes.");
        }

        byte[] result = new byte[2 + value.Length];
        result[0] = itemId;
        result[1] = (byte)value.Length;
        value.CopyTo(result.AsSpan(2));
        return result;
    }

    public static List<QcyTlvItem> DecodeAll(ReadOnlySpan<byte> data)
    {
        var items = new List<QcyTlvItem>();
        int offset = 0;

        while (offset + 2 <= data.Length)
        {
            byte id = data[offset];
            byte len = data[offset + 1];
            if (offset + 2 + len > data.Length)
            {
                break;
            }

            byte[] value = len > 0 ? data.Slice(offset + 2, len).ToArray() : [];
            items.Add(new QcyTlvItem(id, value));
            offset += 2 + len;
        }

        return items;
    }

    /// <summary>
    /// Remove o cabeçalho [0xFF, bodyLen] do protocolo QCY A001 quando presente (útil para vetores N70).
    /// </summary>
    public static ReadOnlySpan<byte> UnwrapA001Envelope(ReadOnlySpan<byte> packet)
    {
        if (packet.Length >= 2 && packet[0] == 0xFF && packet[1] == packet.Length - 2)
        {
            return packet[2..];
        }
        return packet;
    }
}
