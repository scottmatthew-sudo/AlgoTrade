using Xunit;

using AlgoTrade.Core.Domain;
using AlgoTrade.Core;

namespace AlgoTrade.Tests;

public class IndicatorTests
{
    [Fact]
    public void Sma_Basic_Windows()
    {
        var r = Indicators.Sma(new[] { 2.0, 4, 6, 8 }, 2);
        Assert.Null(r[0]);
        Assert.Equal(3, r[1]!.Value, 9);
        Assert.Equal(5, r[2]!.Value, 9);
        Assert.Equal(7, r[3]!.Value, 9);
    }

    [Fact]
    public void Rsi_Period3_Hand_Computed_Oracle()
    {
        // closes 1,2,3,4,3,2,1 — Wilder RSI(3), worked out on paper:
        //   i=3: avgGain=1, avgLoss=0            → RSI = 100
        //   i=4: avgGain=2/3, avgLoss=1/3, RS=2  → RSI = 66.666…
        //   i=5: avgGain=4/9, avgLoss=5/9, RS=.8 → RSI = 44.444…
        //   i=6: avgGain=8/27, avgLoss=19/27     → RSI = 29.6296…
        var r = Indicators.Rsi(new[] { 1.0, 2, 3, 4, 3, 2, 1 }, 3);
        Assert.Null(r[0]); Assert.Null(r[1]); Assert.Null(r[2]);
        Assert.Equal(100, r[3]!.Value, 6);
        Assert.Equal(66.666666, r[4]!.Value, 4);
        Assert.Equal(44.444444, r[5]!.Value, 4);
        Assert.Equal(29.62963, r[6]!.Value, 4);
    }

    [Fact]
    public void Rsi_All_Gains_Is_100_All_Losses_Is_0()
    {
        var up = Indicators.Rsi(new[] { 1.0, 2, 3, 4, 5, 6 }, 3);
        Assert.Equal(100, up[^1]!.Value, 6);
        var down = Indicators.Rsi(new[] { 6.0, 5, 4, 3, 2, 1 }, 3);
        Assert.Equal(0, down[^1]!.Value, 6);
    }

    /* Donchian windows EXCLUDE the current candle (mirrors js) */
    [Fact]
    public void Donchian_Window_Excludes_Current_Candle()
    {
        var candles = new List<Candle>
        {
            new("2025-01-01T00:00:00", 10, 10, 9, 10),
            new("2025-01-01T00:01:00", 10, 12, 9, 11),
            new("2025-01-01T00:02:00", 11, 11, 8, 9),
            new("2025-01-01T00:03:00", 9, 20, 9, 19),   // its own high (20) must NOT count
        };
        Assert.Equal(12, Indicators.HighestHigh(candles, 3, 3), 9);   // max of first 3 highs
        Assert.Equal(8, Indicators.LowestLow(candles, 3, 3), 9);      // min of first 3 lows
        Assert.Equal(12, Indicators.HighestHigh(candles, 3, 100), 9); // capped at series start
    }
}
