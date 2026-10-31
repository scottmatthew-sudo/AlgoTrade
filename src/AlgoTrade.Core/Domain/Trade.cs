using System.Text.Json.Serialization;

namespace AlgoTrade.Core.Domain;

/* ═══════════════════════════════════════════════════════════════
   Trade — one completed round-trip (Blueprint §4.4).

   Wire format (FROZEN): { no, dir, entryTs, entry, exitTs, exit,
                           qty, gross, fees, net, reason }
   reason ∈ 'Signal' | 'StopLoss' | 'TakeProfit' | 'EndOfData'  (D-check)
   dir    = 'Long' (engine v1 is long-only, matching the manual).
   ═══════════════════════════════════════════════════════════════ */
public sealed class Trade
{
    [JsonPropertyName("no")] public int No { get; set; }
    [JsonPropertyName("dir")] public string Dir { get; set; } = "Long";
    [JsonPropertyName("entryTs")] public string EntryTs { get; set; } = "";
    [JsonPropertyName("entry")] public double Entry { get; set; }
    [JsonPropertyName("exitTs")] public string ExitTs { get; set; } = "";
    [JsonPropertyName("exit")] public double Exit { get; set; }
    [JsonPropertyName("qty")] public double Qty { get; set; }
    [JsonPropertyName("gross")] public double Gross { get; set; }
    [JsonPropertyName("fees")] public double Fees { get; set; }
    [JsonPropertyName("net")] public double Net { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = "Signal";

    /// <summary>D14 — a win is a trade with strictly positive NET P/L.</summary>
    [JsonIgnore] public bool IsWin => Net > 0;
}

public static class ExitReasons
{
    public const string Signal = "Signal";
    public const string StopLoss = "StopLoss";
    public const string TakeProfit = "TakeProfit";
    public const string EndOfData = "EndOfData";

    public static readonly string[] All = { Signal, StopLoss, TakeProfit, EndOfData };
}
