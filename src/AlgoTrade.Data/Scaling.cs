using System.Globalization;

namespace AlgoTrade.Data;

/* ═══════════════════════════════════════════════════════════════
   Scaling — the D5 translation layer.

   SQLite (Turso) has no DECIMAL. LOCKED scheme:
     money / prices / volume / qty  → INTEGER × 10⁶
     percent metrics                → INTEGER × 10⁴
   Domain stays double (JS-Number semantics); this file is the ONLY
   place that converts, so the rule stays auditable in one spot.
   ═══════════════════════════════════════════════════════════════ */
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
