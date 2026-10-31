using System.Text.Json.Serialization;

namespace AlgoTrade.Core.Domain;


public sealed class BacktestRun
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("datasetId")] public long DatasetId { get; set; }
    [JsonPropertyName("strategyId")] public long? StrategyId { get; set; }
    [JsonPropertyName("datasetName")] public string DatasetName { get; set; } = "";
    [JsonPropertyName("strategyName")] public string StrategyName { get; set; } = "";
    [JsonPropertyName("runAt")] public string RunAt { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "Completed";
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("executionMs")] public int ExecutionMs { get; set; }

    [JsonPropertyName("equity")] public List<double> Equity { get; set; } = new();
    [JsonPropertyName("trades")] public List<Trade> Trades { get; set; } = new();
    [JsonPropertyName("assumptions")] public Dictionary<string, object?> Assumptions { get; set; } = new();
    [JsonPropertyName("snapshot")] public StrategyConfig? Snapshot { get; set; }
    [JsonPropertyName("m")] public PerformanceMetrics M { get; set; } = new();

    public static Dictionary<string, object?> BuildAssumptions(double initialEquity, double feePerTrade) => new()
    {
        ["initialEquity"] = initialEquity,
        ["fillModel"] = "next open (D8)",
        ["costs"] = feePerTrade > 0 ? $"${feePerTrade}/fill" : "before transaction costs (D11)",
        ["endOfData"] = "force-close at last close (D13)",
    };
}

/* List projection for GET /runs — mirrors runLite() in js/api.js. */
public sealed class RunListRow
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("datasetId")] public long DatasetId { get; set; }
    [JsonPropertyName("strategyId")] public long? StrategyId { get; set; }
    [JsonPropertyName("datasetName")] public string DatasetName { get; set; } = "";
    [JsonPropertyName("strategyName")] public string StrategyName { get; set; } = "";
    [JsonPropertyName("runAt")] public string RunAt { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "Completed";
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("executionMs")] public int ExecutionMs { get; set; }
    [JsonPropertyName("assumptions")] public Dictionary<string, object?> Assumptions { get; set; } = new();
    [JsonPropertyName("snapshot")] public StrategyConfig? Snapshot { get; set; }

    [JsonPropertyName("tradeCount")] public int TradeCount { get; set; }
    [JsonPropertyName("netProfit")] public double NetProfit { get; set; }
    [JsonPropertyName("winRatePct")] public double WinRatePct { get; set; }
    [JsonPropertyName("totalTrades")] public int TotalTrades { get; set; }
    [JsonPropertyName("profitFactor")] public double? ProfitFactor { get; set; }
    [JsonPropertyName("maxDrawdownPct")] public double MaxDrawdownPct { get; set; }
    [JsonPropertyName("finalEquity")] public double FinalEquity { get; set; }
    [JsonPropertyName("spark")] public List<double> Spark { get; set; } = new();
}
