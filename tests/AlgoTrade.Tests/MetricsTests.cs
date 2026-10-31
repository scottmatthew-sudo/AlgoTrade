using Xunit;

using AlgoTrade.Core.Domain;
using AlgoTrade.Core.Engine;

namespace AlgoTrade.Tests;

public class MetricsTests
{
    [Fact]
    public void MaxDrawdown_Hand_Sequence()
    {
        // curve 100 → 120 (peak) → 90 (trough) → 110
        // dd at trough = 120-90 = 30 → 30/120 = 25%
        var m = MetricsCalculator.Compute(new List<Trade>(), new[] { 100.0, 120, 90, 110 }, 100);
        Assert.Equal(30, m.MaxDd, 9);
        Assert.Equal(25, m.MaxDdPct, 9);
    }

    [Fact]
    public void ProfitFactor_Null_When_No_Losing_Trade()
    {
        var trades = new List<Trade>
        {
            new() { No = 1, Gross = 10, Net = 10 },
            new() { No = 2, Gross = 3, Net = 3 },
        };
        var m = MetricsCalculator.Compute(trades, new[] { 10000.0, 10013 }, 10000);
        Assert.Null(m.Pf);                       // D15 — N/A, not infinity
        Assert.Equal(2, m.Total);
        Assert.Equal(2, m.Wins);
        Assert.Equal(0, m.Losses);
        Assert.Equal(100, m.WinRate, 9);
    }

    [Fact]
    public void Empty_Trade_List_Is_A_Valid_Result()
    {
        var m = MetricsCalculator.Compute(new List<Trade>(), new[] { 10000.0, 10000.0 }, 10000);
        Assert.Equal(0, m.Total);
        Assert.Equal(0, m.WinRate, 9);
        Assert.Null(m.Pf);
        Assert.Equal(10000, m.Final, 9);
        Assert.Equal(0, m.Net, 9);
        Assert.Equal(0, m.MaxDd, 9);
    }

    [Fact]
    public void Net_Equals_Final_Minus_Initial()
    {
        var equity = new[] { 1000.0, 1100, 900, 1150 };
        var m = MetricsCalculator.Compute(new List<Trade>(), equity, 1000);
        Assert.Equal(150, m.Net, 9);
        Assert.Equal(1150, m.Final, 9);
        Assert.Equal(1150 - 1000, m.Net, 9);
    }
}
