using System.Text.Json.Serialization;

namespace AlgoTrade.Core.Domain;

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
