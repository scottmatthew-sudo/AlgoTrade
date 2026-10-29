using System.Text.Json.Serialization;

namespace AlgoTrade.Core.Domain;

/* ═══════════════════════════════════════════════════════════════
   Candle — the atomic unit of market data (Blueprint §4.1).

   Wire format (FROZEN, matches js/mock-data.js + charts.js):
     { ts: "2025-01-01T14:00:00", o, h, l, c, v }
   ts is UTC formatted "yyyy-MM-ddTHH:mm:ss" (sortable, no Z —
   mirrors the browser mock's toISOString().slice(0,19)).

   DB: prices/volume stored as INTEGER scaled by 1e6 (LOCKED D5).
   Domain keeps double — identical to JS Number semantics.
   ═══════════════════════════════════════════════════════════════ */
public sealed class Candle
{
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("o")] public double O { get; set; }
    [JsonPropertyName("h")] public double H { get; set; }
    [JsonPropertyName("l")] public double L { get; set; }
    [JsonPropertyName("c")] public double C { get; set; }
    [JsonPropertyName("v")] public double V { get; set; }

    public Candle() { }

    public Candle(string ts, double o, double h, double l, double c, double v)
    { Ts = ts; O = o; H = h; L = l; C = c; V = v; }

    public static string ToIso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss");

    /// <summary>Parses ISO ("yyyy-MM-ddTHH:mm:ss[.fff][Z]") or "yyyy-MM-dd HH:mm:ss" to a UTC DateTime.</summary>
    public static DateTime ParseTs(string ts)
    {
        var s = ts.Trim();
        if (s.EndsWith("Z")) s = s[..^1];
        if (s.Length > 19) s = s[..19];
        return DateTime.ParseExact(s, new[] { "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" },
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal);
    }
}
