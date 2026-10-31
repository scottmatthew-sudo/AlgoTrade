using System.Text.Json.Serialization;

namespace AlgoTrade.Core.Domain;

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
