using System.Buffers.Binary;

namespace N50.Core.Protocol;

public readonly record struct BatteryByte(int? Percent, bool IsCharging)
{
    // Bit 7 = carregando; bits 0..6 = percentual válido apenas em 1..100 (Devicebind.refreshInfoFromAVData)
    public static BatteryByte Decode(byte raw)
    {
        int percent = raw & 0x7F;
        if (percent is < 1 or > 100)
        {
            return new BatteryByte(null, false);
        }

        bool charging = (raw & 0x80) != 0;
        return new BatteryByte(percent, charging);
    }
}

public sealed record QcyAdvertisementData(
    ushort VendorId,
    byte ColorIndex,
    BatteryByte Left,
    BatteryByte Right,
    BatteryByte Case,
    string ClassicMac,
    string OtherMac,
    byte[] RawPayload)
{
    public bool IsJieLi => (VendorId & 0x00F0) == 0x0070 || VendorId == 512;
    public bool IsNewJieLi => (VendorId & 0x80F0) == 0x8070;
    public bool IsN50 => VendorId is QcyUuids.VendorIdN50 or QcyUuids.VendorIdHt12N50;

    // EarphoneListManager.updateDeviceBleMac ignora anúncios quando ambos os fones estão sem bateria válida
    public bool IsNoBattery => Left.Percent is null && Right.Percent is null;
}

public static class QcyAdvertisementParser
{
    public const int MinPayloadLength = 20;
    public const int FullPayloadLength = 24;

    public static bool TryParse(ReadOnlySpan<byte> payload, out QcyAdvertisementData? result)
    {
        result = null;
        if (payload.Length < MinPayloadLength)
        {
            return false;
        }

        // O APK preenche até 24 bytes com zeros quando o anúncio vem com 20..23 bytes
        Span<byte> padded = stackalloc byte[FullPayloadLength];
        payload[..Math.Min(payload.Length, FullPayloadLength)].CopyTo(padded);

        ushort vendorId = BinaryPrimitives.ReadUInt16BigEndian(padded[..2]);
        // APK Devicebind.refreshInfoFromAVData usa (b[3] & 0x0E) >> 1 (Quicky documentou 0x18 por engano)
        byte colorIndex = (byte)((padded[3] & 0x0E) >> 1);

        var left = BatteryByte.Decode(padded[5]);
        var right = BatteryByte.Decode(padded[6]);
        var caseBat = BatteryByte.Decode(padded[7]);

        string classicMac = FormatPermutedMac(padded[12], padded[11], padded[13], padded[16], padded[15], padded[14]);
        string otherMac = FormatPermutedMac(padded[19], padded[18], padded[20], padded[23], padded[22], padded[21]);
        if (otherMac == "00:00:00:00:00:00")
        {
            otherMac = classicMac;
        }

        result = new QcyAdvertisementData(
            vendorId,
            colorIndex,
            left,
            right,
            caseBat,
            classicMac,
            otherMac,
            payload.ToArray());
        return true;
    }

    private static string FormatPermutedMac(byte b0, byte b1, byte b2, byte b3, byte b4, byte b5) =>
        $"{b0:X2}:{b1:X2}:{b2:X2}:{b3:X2}:{b4:X2}:{b5:X2}";
}
