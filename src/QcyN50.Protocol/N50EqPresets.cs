namespace N50.Core.Protocol;

public sealed record N50EqPreset(string Id, string Name, byte EqIndex, double[] TenBandGainsDb);

/// <summary>
/// Presets e mapeamento de 10 bandas (±8 dB) para o EQ Paramétrico v2 (Custom 0x22)
/// confirmado no hardware QCY MeloBuds N50 (firmware V_0.5.2.1).
/// Frequências oficiais do catálogo N50 (Quicky / PairFree): 32..16000 Hz.
/// </summary>
public static class N50EqPresets
{
    public static readonly ushort[] StandardFrequenciesHz =
        [32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public static readonly IReadOnlyList<N50EqPreset> BuiltIn =
    [
        new("default",    "Padrão",        0x00, [ 0,  0,  0,  0,  0,  0,  0,  0,  0,  0]),
        new("pop",        "Pop",           0x01, [-1,  1,  3,  4,  2,  0, -1,  1,  2,  2]),
        new("heavy_bass", "Graves Pesados",0x02, [ 6,  5,  4,  2,  0, -1,  0,  1,  1,  0]),
        new("rock",       "Rock",          0x03, [ 4,  3,  1, -1, -2,  0,  2,  3,  4,  4]),
        new("soft",       "Suave",         0x04, [ 1,  1,  0,  0,  0, -1, -2, -2, -1,  0]),
        new("classic",    "Clássico",      0x05, [ 3,  2,  1,  0,  0,  0, -1,  1,  2,  3]),
    ];

    public static IReadOnlyList<N50ParametricBand> FromTenBandGains(ReadOnlySpan<double> gainsDb)
    {
        if (gainsDb.Length != StandardFrequenciesHz.Length)
        {
            throw new ArgumentException("São necessárias exatamente 10 bandas.", nameof(gainsDb));
        }

        var bands = new N50ParametricBand[StandardFrequenciesHz.Length];
        for (int i = 0; i < StandardFrequenciesHz.Length; i++)
        {
            double clamped = Math.Clamp(gainsDb[i], -8.0, 8.0);
            // filterType 0x02 = peaking EQ (confirmado no payload 0x22 real do N50 V_0.5.2.1)
            bands[i] = new N50ParametricBand(StandardFrequenciesHz[i], clamped, 1.41, 0x02);
        }
        return bands;
    }

    /// <summary>
    /// Calcula ganho mestre compensatório (negativo quando há boost) para evitar clipping DSP.
    /// </summary>
    public static double ComputeSafeMasterGain(ReadOnlySpan<double> gainsDb)
    {
        double maxPositive = 0.0;
        foreach (double g in gainsDb)
        {
            if (g > maxPositive) maxPositive = g;
        }
        return maxPositive > 0.0 ? -Math.Min(maxPositive, 8.0) : 0.0;
    }

    /// <summary>
    /// Projeta qualquer EQ paramétrico lido do fone para as 10 barras de exibição aproximadas.
    /// </summary>
    public static double[] ApproximateTenBandGains(N50ParametricEqState? eq)
    {
        double[] result = new double[StandardFrequenciesHz.Length];
        if (eq is null || eq.Bands.Count == 0)
        {
            return result;
        }

        foreach (var band in eq.Bands)
        {
            int closestIdx = 0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < StandardFrequenciesHz.Length; i++)
            {
                double dist = Math.Abs(Math.Log2(Math.Max(1.0, band.FrequencyHz) / StandardFrequenciesHz[i]));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    closestIdx = i;
                }
            }
            result[closestIdx] = Math.Clamp(band.GainDb, -8.0, 8.0);
        }

        return result;
    }
}
