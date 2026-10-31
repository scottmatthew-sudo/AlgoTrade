using System.Text.Json.Serialization;

namespace AlgoTrade.Core.Domain;

/* ═══════════════════════════════════════════════════════════════
   StrategyConfig — versioned strategy wire object (FROZEN).

   Browser shape (js/store.js):
     { id, name, type, desc, p: {...}, risk: {...}, version,
       createdAt, updatedAt }
   type ∈ 'sma' | 'donchian' | 'rsi'
   p    ∈ { fast, slow } | { entry, exit } | { period, os, ob }

   D23 — every BacktestRun stores a full JSON snapshot of the
   config it ran with, so later edits never rewrite history.
   ═══════════════════════════════════════════════════════════════ */
public sealed class StrategyConfig
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "sma";
    [JsonPropertyName("desc")] public string? Desc { get; set; }
    [JsonPropertyName("p")] public Dictionary<string, object?> P { get; set; } = new();
    [JsonPropertyName("risk")] public RiskSettings Risk { get; set; } = new();
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }

    /* ── typed parameter accessors (numbers may arrive as int/double) ── */
    public int Int(string key, int fallback = 0)
    {
        if (!P.TryGetValue(key, out var v) || v is null) return fallback;
        if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
            return (int)System.Math.Round(je.GetDouble());
        if (v is int i) return i;
        if (v is long l) return (int)l;
        if (v is double d) return (int)System.Math.Round(d);
        return int.TryParse(v.ToString(), out var parsed) ? parsed : fallback;
    }
}

/* ═══════════════════════════════════════════════════════════════
   RiskSettings — D10–D11: sizing, protective exits, flat fees.
   slPct / tpPct null ⇒ protective exit disabled.
   ═══════════════════════════════════════════════════════════════ */
public sealed class RiskSettings
{
    [JsonPropertyName("sizing")] public string Sizing { get; set; } = "percent";
    [JsonPropertyName("percentOfEquity")] public double PercentOfEquity { get; set; } = 10;
    [JsonPropertyName("fixedUnits")] public double FixedUnits { get; set; } = 1;
    [JsonPropertyName("slPct")] public double? SlPct { get; set; }
    [JsonPropertyName("tpPct")] public double? TpPct { get; set; }
    [JsonPropertyName("feePerTrade")] public double FeePerTrade { get; set; } = 0;
}
