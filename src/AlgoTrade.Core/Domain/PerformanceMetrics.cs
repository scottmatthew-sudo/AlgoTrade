using System.Text.Json.Serialization;

namespace AlgoTrade.Core.Domain;

/* ═══════════════════════════════════════════════════════════════
   PerformanceMetrics — the `m` object on a run (FROZEN wire names).

   Locked definitions (Blueprint D14–D15, mirrors js metrics()):
     total      completed round trips
     wins       trades with net > 0            (D14)
     losses     trades with net < 0
     breakEven  trades with net == 0
     net        finalEquity - initialEquity
     winRate    wins / total * 100             (percentage 0..100)
     pf         grossProfit / grossLoss, NULL when no losing trade
     maxDd      max peak-to-trough drop of the mark-to-market curve (D12)
     maxDdPct   maxDd / peak * 100 at the trough
   ═══════════════════════════════════════════════════════════════ */
public sealed class PerformanceMetrics
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("wins")] public int Wins { get; set; }
    [JsonPropertyName("losses")] public int Losses { get; set; }
    [JsonPropertyName("breakEven")] public int BreakEven { get; set; }

    [JsonPropertyName("initial")] public double Initial { get; set; }
    [JsonPropertyName("final")] public double Final { get; set; }
    [JsonPropertyName("net")] public double Net { get; set; }
    [JsonPropertyName("grossProfit")] public double GrossProfit { get; set; }
    [JsonPropertyName("grossLoss")] public double GrossLoss { get; set; }

    [JsonPropertyName("winRate")] public double WinRate { get; set; }
    [JsonPropertyName("pf")] public double? Pf { get; set; }

    [JsonPropertyName("maxDd")] public double MaxDd { get; set; }
    [JsonPropertyName("maxDdPct")] public double MaxDdPct { get; set; }
}
