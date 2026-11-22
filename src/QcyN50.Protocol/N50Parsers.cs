using System.Buffers.Binary;
using System.Text;

namespace N50.Core.Protocol;

public static class N50Parsers
{
    public static N50TargetInfo ParseTargetInfo(ReadOnlySpan<byte> paramData)
    {
        string? firmware = null;
        ushort? sendMtu = null;
        ushort? recvMtu = null;
        string? deviceName = null;

        foreach (var item in JieLiLtv.DecodeAll(paramData))
        {
            switch (item.Type)
            {
                case 5 when item.Value.Length >= 2:
                    // Nibbles: V_(b0>>4).(b0&F).(b1>>4).(b1&F) (ParseHelper.parseTargetInfo case 5)
                    byte b0 = item.Value[0];
                    byte b1 = item.Value[1];
                    firmware = $"V_{(b0 >> 4) & 0xF}.{b0 & 0xF}.{(b1 >> 4) & 0xF}.{b1 & 0xF}";
                    break;
                case 13 when item.Value.Length >= 2:
                    sendMtu = BinaryPrimitives.ReadUInt16BigEndian(item.Value.AsSpan(0, 2));
                    if (item.Value.Length >= 4)
                    {
                        recvMtu = BinaryPrimitives.ReadUInt16BigEndian(item.Value.AsSpan(2, 2));
                    }
                    break;
                case 16 when item.Value.Length > 0:
                    deviceName = Encoding.UTF8.GetString(item.Value).TrimEnd('\0');
                    break;
            }
        }

        return new N50TargetInfo(firmware, sendMtu, recvMtu, deviceName);
    }

    public static N50AdvInfo ParseAdvInfo(ReadOnlySpan<byte> paramData)
    {
        N50BatteryState? battery = null;
        string? name = null;
        List<N50TouchMapping>? keys = null;
        byte? workMode = null;
        byte? inEar = null;
        string? language = null;
        byte? sleepMode = null;
        N50LegacyNoiseState? legacyNoise = null;
        byte? balance = null;

        foreach (var item in JieLiLtv.DecodeAll(paramData))
        {
            switch (item.Type)
            {
                case 0 when item.Value.Length >= 3:
                    battery = new N50BatteryState(
                        BatteryByte.Decode(item.Value[0]),
                        BatteryByte.Decode(item.Value[1]),
                        BatteryByte.Decode(item.Value[2]));
                    break;

                case 1 when item.Value.Length > 0:
                    name = Encoding.UTF8.GetString(item.Value).TrimEnd('\0');
                    break;

                case 2 when item.Value.Length >= 3:
                    keys ??= [];
                    for (int i = 0; i + 2 < item.Value.Length; i += 3)
                    {
                        keys.Add(new N50TouchMapping(item.Value[i], item.Value[i + 1], item.Value[i + 2]));
                    }
                    break;

                case 5 when item.Value.Length >= 1:
                    workMode = item.Value[0];
                    break;

                case 8 when item.Value.Length >= 1:
                    inEar = item.Value[0];
                    break;

                case 9 when item.Value.Length > 0:
                    language = Encoding.UTF8.GetString(item.Value).TrimEnd('\0');
                    break;

                case 10 when item.Value.Length >= 1:
                    sleepMode = item.Value[0];
                    break;

                case 11 when item.Value.Length >= 8:
                    legacyNoise = new N50LegacyNoiseState(
                        item.Value[0],
                        BinaryPrimitives.ReadUInt16BigEndian(item.Value.AsSpan(2, 2)),
                        BinaryPrimitives.ReadUInt16BigEndian(item.Value.AsSpan(4, 2)),
                        BinaryPrimitives.ReadUInt16BigEndian(item.Value.AsSpan(6, 2)));
                    break;

                case 12 when item.Value.Length >= 1:
                    balance = item.Value[0];
                    break;
            }
        }

        return new N50AdvInfo(battery, name, keys, workMode, inEar, language, sleepMode, legacyNoise, balance);
    }

    public static N50BatteryState? ParseAdvNotifyBattery(ReadOnlySpan<byte> paramData)
    {
        // ParseHelper.parseNotifyADVInfo: bytes 14..16 carregam L, R e Estojo
        if (paramData.Length < 17)
        {
            return null;
        }

        return new N50BatteryState(
            BatteryByte.Decode(paramData[14]),
            BatteryByte.Decode(paramData[15]),
            BatteryByte.Decode(paramData[16]));
    }

    public static N50GraphicEqState? ParseGraphicEq(ReadOnlySpan<byte> paramData)
    {
        if (paramData.Length < 2)
        {
            return null;
        }

        // paramData[0] = func (0xFF), seguido de LTVs
        foreach (var item in JieLiLtv.DecodeAll(paramData[1..]))
        {
            if (item.Type == 0x04 && item.Value.Length >= 2)
            {
                byte mode = item.Value[0];
                int bandCount = item.Value.Length - 1;
                var gains = new sbyte[bandCount];
                for (int i = 0; i < bandCount; i++)
                {
                    gains[i] = unchecked((sbyte)item.Value[1 + i]);
                }
                return new N50GraphicEqState(mode, gains);
            }
        }

        return null;
    }

    public static N50AncState? ParseAncItem(ReadOnlySpan<byte> value)
    {
        if (value.Length < 3)
        {
            return null;
        }

        byte mode = value[0];
        byte scene = value[1];
        byte level = value[2];
        // Devicebind.getCurrentANCModeIndex remapeia (3, 2, *) para (3, 1, 0)
        if (mode == 0x03 && scene == 0x02)
        {
            scene = 0x01;
            level = 0x00;
        }

        return new N50AncState(mode, scene, level);
    }

    public static N50AutoOffState? ParseAutoOffItem(ReadOnlySpan<byte> value)
    {
        if (value.Length < 4)
        {
            return null;
        }

        ushort configured = BinaryPrimitives.ReadUInt16LittleEndian(value[..2]);
        ushort remaining = BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(2, 2));
        return new N50AutoOffState(configured, remaining);
    }

    public static N50PromptVolumeState? ParsePromptVolumeItem(ReadOnlySpan<byte> value)
    {
        if (value.Length < 1)
        {
            return null;
        }

        byte vol = value[0];
        byte max = value.Length >= 2 ? value[1] : (byte)100;
        return new N50PromptVolumeState(vol, max);
    }

    public static N50ParametricEqState? ParseParametricEqItem(byte itemId, ReadOnlySpan<byte> value)
    {
        int stride = itemId == 0x22 ? 7 : itemId == 0x20 ? 6 : 0;
        if (stride == 0 || value.Length < 3)
        {
            return null;
        }

        byte eqIdx = value[0];
        double masterDb = BinaryPrimitives.ReadInt16LittleEndian(value.Slice(1, 2)) / 100.0;
        int remaining = value.Length - 3;
        int bandCount = remaining / stride;
        var bands = new List<N50ParametricBand>(bandCount);

        for (int i = 0; i < bandCount; i++)
        {
            int off = 3 + i * stride;
            ushort freq = BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(off, 2));
            double gainDb = BinaryPrimitives.ReadInt16LittleEndian(value.Slice(off + 2, 2)) / 100.0;
            double q = BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(off + 4, 2)) / 100.0;
            byte? filterType = stride == 7 ? value[off + 6] : null;
            bands.Add(new N50ParametricBand(freq, gainDb, q, filterType));
        }

        return new N50ParametricEqState(eqIdx, masterDb, bands);
    }
}
