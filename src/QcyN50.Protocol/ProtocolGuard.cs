namespace N50.Core.Protocol;

public sealed class ProtocolGuardException : InvalidOperationException
{
    public ProtocolGuardException(string message) : base(message) { }
}

/// <summary>
/// Barreira obrigatória de segurança (default-deny) aplicada sobre o frame RCSP final
/// imediatamente antes de qualquer escrita BLE. Impede o envio de comandos destrutivos,
/// OTA/DFU, reset, renomeação ou opcodes não mapeados.
/// </summary>
public static class ProtocolGuard
{
    private static readonly HashSet<byte> AllowedAckOpCodes =
    [
        RcspOpCode.GetTargetInfo,
        RcspOpCode.GetSysInfo,
        RcspOpCode.SetSysInfo,
        RcspOpCode.SysInfoAutoUpdate,
        RcspOpCode.AdvSet,
        RcspOpCode.AdvGet,
        RcspOpCode.AdvNotify,
        RcspOpCode.AdvRequestOperation,
        RcspOpCode.Custom
    ];

    // Atributos ADV_SET (0xC0) explicitamente permitidos (exclui 1=Name, 3=LED, 4=MIC, 6=Product, 9=Language)
    private static readonly HashSet<byte> AllowedAdvSetAttrs =
    [
        0x02, // Touch keys
        0x05, // WorkMode (Game Mode)
        0x08, // In-Ear
        0x0A, // Sleep Mode
        0x0B, // Legacy Noise Mode
        0x0C  // L/R Balance
    ];

    // Itens QCY Custom (0xFF) estritamente proibidos (leitura ou escrita)
    private static readonly HashSet<byte> ForbiddenCustomItems =
    [
        0x01, // RESET_DEFAULT
        0x02, // CLEAR_PAIR
        0x03, // FACTORY_RESET
        0x0D, // TEST_MODE
        0x18, // PAIR_NAME
        0x1E, // TAKE_PHOTO
        0x1F, // STANDBY
        0x25, // RESTART
        0x26, // RESET_KEYS
        0x34, // TWS_ENABLE
        0x3E, // SYNC_TIME
        0x3F  // ALARM
    ];

    // Itens QCY Custom (0xFF) permitidos para escrita controlada
    private static readonly HashSet<byte> AllowedCustomWriteItems =
    [
        0x09, // Game mode (fallback)
        0x0C, // Legacy noise mode
        0x10, // Sleep mode (fallback)
        0x14, // Auto power-off timer (nunca minutes == 0!)
        0x16, // Balance (fallback)
        0x17, // ANC scene/mode (3 bytes)
        0x1D, // Prompt volume
        0x20, // Parametric EQ v1
        0x22, // Parametric EQ v2
        0x24, // Multipoint
        0x2C  // Wearing detection
    ];

    // Itens QCY Custom (0xFF) permitidos para leitura via FE 01 <id>
    private static readonly HashSet<byte> AllowedCustomReadItems =
    [
        0x09, 0x0C, 0x10, 0x14, 0x16, 0x17, 0x1D, 0x20, 0x22, 0x24, 0x2C, 0x2F, 0x30
    ];

    public static void ValidateOutboundFrame(ReadOnlySpan<byte> rawFrame)
    {
        if (!RcspFramer.TryParseSingle(rawFrame, out var frame) || frame is null)
        {
            throw new ProtocolGuardException("Frame RCSP inválido ou incompleto rejeitado pelo ProtocolGuard.");
        }

        if (frame.IsResponse)
        {
            if (frame.Flags != RcspFramer.FlagResponse || frame.ParamData.Length != 0 || frame.Status != 0x00 ||
                !AllowedAckOpCodes.Contains(frame.OpCode))
            {
                throw new ProtocolGuardException($"Resposta/ACK RCSP não permitida: opCode=0x{frame.OpCode:X2}, flags=0x{frame.Flags:X2}.");
            }
            return;
        }

        if (frame.Flags is not (RcspFramer.FlagCommandWithResponse or RcspFramer.FlagCommandNoResponse))
        {
            throw new ProtocolGuardException($"Flags RCSP não permitidas: 0x{frame.Flags:X2}.");
        }

        switch (frame.OpCode)
        {
            case RcspOpCode.GetTargetInfo:
                if (frame.ParamData.Length != 5)
                {
                    throw new ProtocolGuardException("GET_TARGET_INFO (0x03) exige exatamente 5 bytes de parâmetro (mask4 + platform1).");
                }
                return;

            case RcspOpCode.AdvGet:
                if (frame.ParamData.Length != 4)
                {
                    throw new ProtocolGuardException("ADV_GET (0xC1) exige exatamente 4 bytes de máscara.");
                }
                return;

            case RcspOpCode.GetSysInfo:
                if (frame.ParamData.Length != 5)
                {
                    throw new ProtocolGuardException("GET_SYS_INFO (0x07) exige exatamente 5 bytes (func1 + mask4).");
                }
                return;

            case RcspOpCode.AdvSet:
                ValidateAdvSet(frame.ParamData);
                return;

            case RcspOpCode.SetSysInfo:
                ValidateSetSysInfo(frame.ParamData);
                return;

            case RcspOpCode.Custom:
                ValidateCustom(frame.ParamData);
                return;

            default:
                throw new ProtocolGuardException(
                    $"Opcode RCSP 0x{frame.OpCode:X2} bloqueado pela política default-deny do ProtocolGuard.");
        }
    }

    private static void ValidateAdvSet(ReadOnlySpan<byte> paramData)
    {
        if (paramData.Length < 2)
        {
            throw new ProtocolGuardException("ADV_SET (0xC0) vazio rejeitado.");
        }

        var items = JieLiLtv.DecodeAll(paramData);
        int consumed = items.Sum(i => 2 + i.Value.Length);
        if (items.Count == 0 || consumed != paramData.Length)
        {
            throw new ProtocolGuardException("ADV_SET (0xC0) contém LTV malformado.");
        }

        foreach (var item in items)
        {
            if (!AllowedAdvSetAttrs.Contains(item.Type))
            {
                throw new ProtocolGuardException(
                    $"Atributo ADV_SET 0x{item.Type:X2} bloqueado pelo ProtocolGuard (ex.: renomeação/LED/idioma proibidos).");
            }
        }
    }

    private static void ValidateSetSysInfo(ReadOnlySpan<byte> paramData)
    {
        // Apenas EQ público: func = 0xFF, LTV único type = 0x04 com 11 bytes (mode + 10 bandas)
        if (paramData.Length != 14 || paramData[0] != 0xFF || paramData[1] != 0x0C || paramData[2] != 0x04)
        {
            throw new ProtocolGuardException(
                "SET_SYS_INFO (0x08) permite exclusivamente escrita de EQ público (func=0xFF, attr=0x04, 10 bandas).");
        }
    }

    private static void ValidateCustom(ReadOnlySpan<byte> paramData)
    {
        if (paramData.Length < 2)
        {
            throw new ProtocolGuardException("CUSTOM (0xFF) vazio rejeitado.");
        }

        var items = QcyTlv.DecodeAll(paramData);
        int consumed = items.Sum(i => 2 + i.Value.Length);
        if (items.Count == 0 || consumed != paramData.Length)
        {
            throw new ProtocolGuardException("CUSTOM (0xFF) contém TLV malformado.");
        }

        foreach (var item in items)
        {
            if (item.Id == 0xFE)
            {
                if (item.Value.Length != 1)
                {
                    throw new ProtocolGuardException("Consulta CUSTOM 0xFE exige exatamente 1 byte de itemId.");
                }

                byte queried = item.Value[0];
                if (ForbiddenCustomItems.Contains(queried) || !AllowedCustomReadItems.Contains(queried))
                {
                    throw new ProtocolGuardException(
                        $"Consulta CUSTOM 0xFE para item 0x{queried:X2} bloqueada pelo ProtocolGuard.");
                }
                continue;
            }

            if (ForbiddenCustomItems.Contains(item.Id) || !AllowedCustomWriteItems.Contains(item.Id))
            {
                throw new ProtocolGuardException(
                    $"Escrita CUSTOM item 0x{item.Id:X2} bloqueada pelo ProtocolGuard.");
            }

            if (item.Id == 0x14)
            {
                if (item.Value.Length != 4 || (item.Value[0] == 0x00 && item.Value[1] == 0x00))
                {
                    throw new ProtocolGuardException(
                        "Item CUSTOM 0x14 (Auto-Off) com 0 minutos bloqueado — desligaria os fones imediatamente.");
                }
            }
        }
    }
}
