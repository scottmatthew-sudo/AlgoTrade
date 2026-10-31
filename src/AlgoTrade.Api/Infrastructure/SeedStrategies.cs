using AlgoTrade.Core.Domain;

namespace AlgoTrade.Api.Infrastructure;


public static class SeedStrategies
{
    public static IEnumerable<StrategyConfig> Defaults() => new[]
    {
        new StrategyConfig
        {
            Name = "SMA Golden Cross 10/30",
            Type = "sma",
            Desc = "Classic trend following — long when fast SMA crosses above slow SMA.",
            P = new() { ["fast"] = 10, ["slow"] = 30 },
            Risk = new RiskSettings { Sizing = "percent", PercentOfEquity = 10, FixedUnits = 1, SlPct = 4, TpPct = null, FeePerTrade = 0 },
        },
        new StrategyConfig
        {
            Name = "Donchian Breakout 20/10",
            Type = "donchian",
            Desc = "Turtle-style breakout — enter on N-bar high, exit on M-bar low.",
            P = new() { ["entry"] = 20, ["exit"] = 10 },
            Risk = new RiskSettings { Sizing = "percent", PercentOfEquity = 15, FixedUnits = 1, SlPct = 3, TpPct = 9, FeePerTrade = 0 },
        },
        new StrategyConfig
        {
            Name = "RSI Reversion 14 (28/72)",
            Type = "rsi",
            Desc = "Mean reversion — buy oversold relief, exit into overbought.",
            P = new() { ["period"] = 14, ["os"] = 28, ["ob"] = 72 },
            Risk = new RiskSettings { Sizing = "fixed", PercentOfEquity = 10, FixedUnits = 0.5, SlPct = 2.5, TpPct = 5, FeePerTrade = 0 },
        },
    };
}
