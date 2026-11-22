namespace N50.Core.Protocol;

public sealed record N50BatteryState(BatteryByte Left, BatteryByte Right, BatteryByte Case);

public sealed record N50AncState(byte Mode, byte Scene, byte Level)
{
    public int Packed => (Mode << 16) | (Scene << 8) | Level;

    public string Describe() => (Mode, Scene, Level) switch
    {
        (0x02, _, _) => $"Normal (02 {Scene:X2} {Level:X2})",
        (0x01, 0x05, _) => $"ANC Adaptativo (01 05 {Level:X2})",
        (0x01, 0x01, var l) => $"ANC Cena 01 [Noisy N50 / Indoor N70] força {l} (01 01 {l:X2})",
        (0x01, 0x02, var l) => $"ANC Transporte força {l} (01 02 {l:X2})",
        (0x01, 0x03, var l) => $"ANC Cena 03 [Indoor N50 / Noisy N70] força {l} (01 03 {l:X2})",
        (0x01, 0x04, _) => $"ANC Anti-vento (01 04 {Level:X2})",
        (0x03, _, var l) => $"Transparência nível {l} (03 {Scene:X2} {l:X2})",
        _ => $"Desconhecido ({Mode:X2} {Scene:X2} {Level:X2})"
    };
}

public sealed record N50TouchMapping(byte Side, byte Gesture, byte ActionJl)
{
    public bool IsSingleEarbudMode => (Gesture & 0x80) != 0;
    public byte BaseGesture => (byte)(Gesture & 0x7F);

    public string SideName => Side switch { 1 => "L", 2 => "R", _ => $"Side({Side})" };
    public string GestureName => BaseGesture switch
    {
        1 => IsSingleEarbudMode ? "1x (1 fone)" : "1x",
        2 => IsSingleEarbudMode ? "2x (1 fone)" : "2x",
        3 => IsSingleEarbudMode ? "3x (1 fone)" : "3x",
        4 => IsSingleEarbudMode ? "4x (1 fone)" : "4x",
        5 => IsSingleEarbudMode ? "Hold (1 fone)" : "Hold",
        _ => $"Gesture(0x{Gesture:X2})"
    };
    public string ActionName => ActionJl switch
    {
        0x7F or 0x00 => "Nenhuma",
        0x80 => "Padrão",
        0x01 => "Play/Pause",
        0x02 => "Anterior",
        0x03 => "Próxima",
        0x04 => "Assistente de voz",
        0x05 => "Volume +",
        0x06 => "Volume -",
        0x07 => "Modo Jogo",
        0x0A => "ANC (0x0A)",
        0x0B => "Alternar ANC",
        _ => $"Action(0x{ActionJl:X2})"
    };
}

public sealed record N50GraphicEqState(byte Mode, sbyte[] GainsDb);

public sealed record N50ParametricBand(ushort FrequencyHz, double GainDb, double QFactor, byte? FilterType);

public sealed record N50ParametricEqState(byte EqIndex, double MasterGainDb, IReadOnlyList<N50ParametricBand> Bands);

public sealed record N50AutoOffState(ushort ConfiguredMinutes, ushort RemainingMinutes)
{
    public bool IsDisabled => ConfiguredMinutes == 0xFFFF;
}

public sealed record N50PromptVolumeState(byte Volume, byte MaxVolume);

public sealed record N50LegacyNoiseState(byte Mode, ushort MinGain, ushort MaxGain, ushort CurrentGain);

public sealed record N50TargetInfo(string? FirmwareVersion, ushort? SendMtu, ushort? RecvMtu, string? DeviceName);

public sealed record N50AdvInfo(
    N50BatteryState? Battery,
    string? DeviceName,
    IReadOnlyList<N50TouchMapping>? TouchMappings,
    byte? WorkMode,
    byte? InEarState,
    string? Language,
    byte? SleepMode,
    N50LegacyNoiseState? LegacyNoise,
    byte? Balance);
