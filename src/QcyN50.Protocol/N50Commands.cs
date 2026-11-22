using System.Buffers.Binary;

namespace N50.Core.Protocol;

/// <summary>
/// Construtores de frames RCSP para o QCY MeloBuds N50.
/// Todo frame construído é validado pelo ProtocolGuard antes de ser devolvido.
/// </summary>
public static class N50Commands
{
    // --- 0x03 GET_TARGET_INFO ---
    public const uint TargetMaskFirmware = 1u << 5;    // 0x00000020
    public const uint TargetMaskMtu = 1u << 13;        // 0x00002000
    public const uint TargetMaskDeviceName = 1u << 16; // 0x00010000

    public static byte[] BuildGetTargetInfo(byte sn, uint mask, byte platform = 0x00)
    {
        Span<byte> param = stackalloc byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(param[..4], mask);
        param[4] = platform;
        return BuildChecked(RcspOpCode.GetTargetInfo, sn, param);
    }

    // --- 0xC1 ADV_GET ---
    public const uint AdvMaskBattery = 1u << 0;      // 0x00000001
    public const uint AdvMaskDeviceName = 1u << 1;   // 0x00000002
    public const uint AdvMaskTouchKeys = 1u << 2;    // 0x00000004
    public const uint AdvMaskWorkMode = 1u << 5;     // 0x00000020
    public const uint AdvMaskInEar = 1u << 8;        // 0x00000100
    public const uint AdvMaskLanguage = 1u << 9;     // 0x00000200
    public const uint AdvMaskSleepMode = 1u << 10;   // 0x00000400
    public const uint AdvMaskLegacyNoise = 1u << 11; // 0x00000800
    public const uint AdvMaskBalance = 1u << 12;     // 0x00001000
    public const uint AdvMaskAppBatteryBatch = 0x00000D21; // bits 0, 5, 8, 10, 11 (JLDeviceImpl.updateBattery)

    public static byte[] BuildAdvGet(byte sn, uint mask)
    {
        Span<byte> param = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(param, mask);
        return BuildChecked(RcspOpCode.AdvGet, sn, param);
    }

    // --- 0xC0 ADV_SET ---
    public static byte[] BuildSetGameModeAdv(byte sn, bool enabled)
    {
        byte[] ltv = JieLiLtv.Encode(0x05, [(byte)(enabled ? 0x02 : 0x01)]);
        return BuildChecked(RcspOpCode.AdvSet, sn, ltv);
    }

    public static byte[] BuildSetSleepModeAdv(byte sn, bool enabled)
    {
        byte[] ltv = JieLiLtv.Encode(0x0A, [(byte)(enabled ? 0x01 : 0x02)]);
        return BuildChecked(RcspOpCode.AdvSet, sn, ltv);
    }

    public static byte[] BuildSetBalanceAdv(byte sn, byte value0To100)
    {
        byte clamped = Math.Clamp(value0To100, (byte)0, (byte)100);
        byte[] ltv = JieLiLtv.Encode(0x0C, [clamped]);
        return BuildChecked(RcspOpCode.AdvSet, sn, ltv);
    }

    public static byte[] BuildSetTouchMappingAdv(byte sn, byte side, byte gesture, byte actionJl)
    {
        byte[] ltv = JieLiLtv.Encode(0x02, [side, gesture, actionJl]);
        return BuildChecked(RcspOpCode.AdvSet, sn, ltv);
    }

    // --- 0x07 / 0x08 SYS_INFO (EQ Gráfico 10 bandas) ---
    public static byte[] BuildGetGraphicEq(byte sn)
    {
        Span<byte> param = [0xFF, 0x00, 0x00, 0x00, 0x10];
        return BuildChecked(RcspOpCode.GetSysInfo, sn, param);
    }

    public static byte[] BuildSetGraphicEqCustom(byte sn, ReadOnlySpan<sbyte> tenBandGainsDb)
    {
        if (tenBandGainsDb.Length != 10)
        {
            throw new ArgumentException("EQ gráfico exige exatamente 10 bandas.", nameof(tenBandGainsDb));
        }

        Span<byte> value = stackalloc byte[11];
        value[0] = 0xFF; // custom
        for (int i = 0; i < 10; i++)
        {
            value[1 + i] = unchecked((byte)Math.Clamp(tenBandGainsDb[i], (sbyte)-12, (sbyte)12));
        }

        byte[] ltv = JieLiLtv.Encode(0x04, value);
        byte[] param = new byte[1 + ltv.Length];
        param[0] = 0xFF;
        ltv.CopyTo(param.AsSpan(1));
        return BuildChecked(RcspOpCode.SetSysInfo, sn, param);
    }

    public static byte[] BuildSetGraphicEqPreset(byte sn, byte presetId)
    {
        // JLDeviceImpl.setEQ envia 0x7F em todas as 10 bandas ao selecionar preset interno
        Span<byte> value = [presetId, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F];
        byte[] ltv = JieLiLtv.Encode(0x04, value);
        byte[] param = new byte[1 + ltv.Length];
        param[0] = 0xFF;
        ltv.CopyTo(param.AsSpan(1));
        return BuildChecked(RcspOpCode.SetSysInfo, sn, param);
    }

    // --- 0xFF CUSTOM (Itens QCY) ---
    public static byte[] BuildCustomRead(byte sn, byte qcyItemId)
    {
        byte[] tlv = QcyTlv.Encode(0xFE, [qcyItemId]);
        return BuildChecked(RcspOpCode.Custom, sn, tlv);
    }

    public static byte[] BuildSetAncCustom(byte sn, byte mode, byte scene, byte level)
    {
        // Regra especial confirmada em QCYConnectManager.setNoiseMode: (3, 1, 0) -> (3, 2, 0)
        if (mode == 0x03 && scene == 0x01 && level == 0x00)
        {
            scene = 0x02;
        }

        byte[] tlv = QcyTlv.Encode(0x17, [mode, scene, level]);
        return BuildChecked(RcspOpCode.Custom, sn, tlv);
    }

    public static byte[] BuildSetGameModeCustom(byte sn, bool enabled)
    {
        byte[] tlv = QcyTlv.Encode(0x09, [(byte)(enabled ? 0x01 : 0x02)]);
        return BuildChecked(RcspOpCode.Custom, sn, tlv);
    }

    public static byte[] BuildSetMultipointCustom(byte sn, bool enabled)
    {
        byte[] tlv = QcyTlv.Encode(0x24, [(byte)(enabled ? 0x01 : 0x02)]);
        return BuildChecked(RcspOpCode.Custom, sn, tlv);
    }

    public static byte[] BuildSetAutoOffCustom(byte sn, ushort minutes)
    {
        if (minutes == 0)
        {
            throw new ProtocolGuardException("Auto-Off de 0 minutos é estritamente proibido.");
        }

        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(payload[..2], minutes);
        payload[2] = 0x00;
        payload[3] = 0x00;
        byte[] tlv = QcyTlv.Encode(0x14, payload);
        return BuildChecked(RcspOpCode.Custom, sn, tlv);
    }

    public static byte[] BuildSetPromptVolumeCustom(byte sn, byte volume)
    {
        byte[] tlv = QcyTlv.Encode(0x1D, [volume, 0x00]);
        return BuildChecked(RcspOpCode.Custom, sn, tlv);
    }

    /// <summary>
    /// EQ Paramétrico v2 (Custom 0xFF, item 0x22) — confirmado no hardware N50 (firmware V_0.5.2.1).
    /// Quantização Little-Endian: masterGain/gain/Q em centésimos (x100), clamp ±12.70 dB; freq em Hz.
    /// </summary>
    public static byte[] BuildSetParametricEqV2(byte sn, byte eqIndex, double masterGainDb, IReadOnlyList<N50ParametricBand> bands)
    {
        if (bands.Count == 0 || bands.Count > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(bands), "EQ paramétrico v2 aceita entre 1 e 15 bandas.");
        }

        byte[] payload = new byte[3 + bands.Count * 7];
        payload[0] = eqIndex;
        short masterRaw = (short)Math.Clamp(Math.Round(masterGainDb * 100.0), -1270, 1270);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1, 2), masterRaw);

        for (int i = 0; i < bands.Count; i++)
        {
            var b = bands[i];
            int off = 3 + i * 7;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(off, 2), b.FrequencyHz);
            short gainRaw = (short)Math.Clamp(Math.Round(b.GainDb * 100.0), -1270, 1270);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(off + 2, 2), gainRaw);
            ushort qRaw = (ushort)Math.Clamp(Math.Round(b.QFactor * 100.0), 10, 2000);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(off + 4, 2), qRaw);
            payload[off + 6] = b.FilterType ?? 0x02;
        }

        byte[] tlv = QcyTlv.Encode(0x22, payload);
        return BuildChecked(RcspOpCode.Custom, sn, tlv);
    }

    public static byte[] BuildAck(byte opCode, byte sn)
    {
        byte[] frame = RcspFramer.BuildAckResponse(opCode, sn);
        ProtocolGuard.ValidateOutboundFrame(frame);
        return frame;
    }

    private static byte[] BuildChecked(byte opCode, byte sn, ReadOnlySpan<byte> paramData)
    {
        byte[] frame = RcspFramer.BuildCommand(opCode, sn, paramData, expectsResponse: true);
        ProtocolGuard.ValidateOutboundFrame(frame);
        return frame;
    }
}
