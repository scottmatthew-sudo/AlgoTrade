using System.Globalization;

namespace AlgoTrade.Data;


public static class Scaling
{
    public const long MoneyScale = 1_000_000;
    public const long PctScale = 10_000;

    public static long ToI(double v) => (long)Math.Round(v * MoneyScale, MidpointRounding.AwayFromZero);
    public static double FromI(long i) => i / (double)MoneyScale;

    public static long ToPctI(double pct) => (long)Math.Round(pct * PctScale, MidpointRounding.AwayFromZero);
    public static double FromPctI(long i) => i / (double)PctScale;

    /* nullable variants (SL/TP/PF can be NULL) */
    public static long? ToINull(double? v) => v is null ? null : ToI(v.Value);
    public static double? FromINull(long? v) => v is null ? null : FromI(v.Value);
    public static long? ToPctINull(double? v) => v is null ? null : ToPctI(v.Value);
    public static double? FromPctINull(long? v) => v is null ? null : FromPctI(v.Value);
}
