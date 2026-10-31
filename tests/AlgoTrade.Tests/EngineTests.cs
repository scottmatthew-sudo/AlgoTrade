using Xunit;

using AlgoTrade.Core.Domain;
using AlgoTrade.Core.Engine;

namespace AlgoTrade.Tests;

/* ═══════════════════════════════════════════════════════════════
   Engine oracle tests — hand-computed scenarios that pin the
   LOCKED rules D7/D8, D9, D10, D11, D12, D13, D14. If someone
   "optimizes" the engine into look-ahead territory, these fail.
   ═══════════════════════════════════════════════════════════════ */
public class EngineTests
{
    private static Candle C(string ts, double o, double h, double l, double c, double v = 100)
        => new(ts, o, h, l, c, v);

    private static StrategyConfig Sma(double pct = 100, double? sl = null, double? tp = null, double fee = 0,
        int fast = 1, int slow = 2) => new()
    {
        Name = "test", Type = "sma",
        P = new Dictionary<string, object?> { ["fast"] = fast, ["slow"] = slow },
        Risk = new RiskSettings { Sizing = "percent", PercentOfEquity = pct, SlPct = sl, TpPct = tp, FeePerTrade = fee },
    };

    /* ── D7/D8: signal on close i ⇒ fill at open of i+1 — never earlier ── */
    [Fact]
    public void Signal_Fills_At_Next_Open_Not_Current()
    {
        // closes 10,10,20 with sma(1)/sma(2): crossUp at i=2 → fill at open[3]
        var candles = new List<Candle>
        {
            C("2025-01-01T00:00:00", 10, 10, 10, 10),
            C("2025-01-01T00:01:00", 10, 10, 10, 10),
            C("2025-01-01T00:02:00", 10, 20, 10, 20),
            C("2025-01-01T00:03:00", 21, 21, 21, 21),   // entry MUST be 21 (open[3]), not 20 (close[2])
            C("2025-01-01T00:04:00", 22, 22, 22, 22),
        };
        var outp = BacktestEngine.Run(candles, Sma(), 10000);

        var trade = Assert.Single(outp.Trades);
        Assert.Equal(21, trade.Entry, 9);
        Assert.Equal("2025-01-01T00:03:00", trade.EntryTs);
        Assert.Equal(ExitReasons.EndOfData, trade.Reason);          // D13
        Assert.Equal(22, trade.Exit, 9);
    }

    /* ── D9: stop-first when SL and TP are both touched in one candle ── */
    [Fact]
    public void Same_Candle_Stop_And_Target_Stop_Wins()
    {
        // enter at open[3]=100 (sl 5% → 95, tp 5% → 105); candle 4 hits both: l=90, h=110
        var candles = new List<Candle>
        {
            C("2025-01-01T00:00:00", 10, 10, 10, 10),
            C("2025-01-01T00:01:00", 10, 10, 10, 10),
            C("2025-01-01T00:02:00", 10, 20, 10, 20),
            C("2025-01-01T00:03:00", 100, 100, 100, 100),  // entry fill = 100
            C("2025-01-01T00:04:00", 100, 110, 90, 100),   // stop 95 AND target 105 both inside
            C("2025-01-01T00:05:00", 100, 100, 100, 100),
        };
        var outp = BacktestEngine.Run(candles, Sma(sl: 5, tp: 5), 10000);

        var trade = Assert.Single(outp.Trades);
        Assert.Equal(ExitReasons.StopLoss, trade.Reason);            // stop-first (D9)
        Assert.Equal(95, trade.Exit, 9);                             // min(open=100, stop=95)
        Assert.Equal(100, trade.Entry, 9);
    }

    /* ── D9 gap handling: open beyond the stop fills AT the open ── */
    [Fact]
    public void Gap_Through_Stop_Fills_At_Open()
    {
        var candles = new List<Candle>
        {
            C("2025-01-01T00:00:00", 10, 10, 10, 10),
            C("2025-01-01T00:01:00", 10, 10, 10, 10),
            C("2025-01-01T00:02:00", 10, 20, 10, 20),
            C("2025-01-01T00:03:00", 100, 100, 100, 100),
            C("2025-01-01T00:04:00", 80, 85, 80, 85),   // opens at 80 — way below stop 95
            C("2025-01-01T00:05:00", 85, 85, 85, 85),
        };
        var outp = BacktestEngine.Run(candles, Sma(sl: 5, tp: 5), 10000);

        var trade = Assert.Single(outp.Trades);
        Assert.Equal(ExitReasons.StopLoss, trade.Reason);
        Assert.Equal(80, trade.Exit, 9);                              // min(open=80, stop=95) = open
    }

    /* ── D10: no leverage — entry rejected when qty*fill > cash ── */
    [Fact]
    public void No_Leverage_Entry_Rejected_When_Cost_Exceeds_Cash()
    {
        // fixed units 1000 @ fill 21 = 21000 > 10000 cash ⇒ no position, no trades
        var candles = new List<Candle>
        {
            C("2025-01-01T00:00:00", 10, 10, 10, 10),
            C("2025-01-01T00:01:00", 10, 10, 10, 10),
            C("2025-01-01T00:02:00", 10, 20, 10, 20),
            C("2025-01-01T00:03:00", 21, 21, 21, 21),
            C("2025-01-01T00:04:00", 22, 22, 22, 22),
        };
        var strat = new StrategyConfig
        {
            Name = "fixed", Type = "sma",
            P = new Dictionary<string, object?> { ["fast"] = 1, ["slow"] = 2 },
            Risk = new RiskSettings { Sizing = "fixed", FixedUnits = 1000 },
        };
        var outp = BacktestEngine.Run(candles, strat, 10000);
        Assert.Empty(outp.Trades);
        Assert.All(outp.Equity, e => Assert.Equal(10000, e, 9));
    }

    /* ── D11: flat fee per fill, charged 2× on exit ── */
    [Fact]
    public void Flat_Fee_Charged_Twice_On_Exit()
    {
        var candles = new List<Candle>
        {
            C("2025-01-01T00:00:00", 10, 10, 10, 10),
            C("2025-01-01T00:01:00", 10, 10, 10, 10),
            C("2025-01-01T00:02:00", 10, 20, 10, 20),
            C("2025-01-01T00:03:00", 21, 21, 21, 21),
            C("2025-01-01T00:04:00", 22, 22, 22, 22),
        };
        var outp = BacktestEngine.Run(candles, Sma(fee: 1), 10000);

        var trade = Assert.Single(outp.Trades);
        double qty = System.Math.Round(10000 / 21, 6);
        Assert.Equal(2, trade.Fees, 9);                                // 1 × 2 fills
        Assert.Equal((22 - 21) * qty - 2, trade.Net, 6);
        Assert.Equal(10000 + trade.Net, outp.Equity[^1], 6);
    }

    /* ── D12: equity marked to the close of every candle ── */
    [Fact]
    public void Equity_Marked_To_Close_Every_Candle()
    {
        var candles = new List<Candle>
        {
            C("2025-01-01T00:00:00", 10, 10, 10, 10),
            C("2025-01-01T00:01:00", 10, 10, 10, 10),
            C("2025-01-01T00:02:00", 10, 20, 10, 20),
            C("2025-01-01T00:03:00", 100, 100, 100, 100),   // entry 100, qty=100 → equity[3]=10000+(100-100)*100
            C("2025-01-01T00:04:00", 102, 102, 102, 102),   // equity[4]=10000+(102-100)*100=10200
            C("2025-01-01T00:05:00", 101, 101, 101, 101),   // equity[5]=10100, EndOfData close
        };
        var outp = BacktestEngine.Run(candles, Sma(), 10000);

        Assert.Equal(10000, outp.Equity[3], 9);
        Assert.Equal(10200, outp.Equity[4], 9);
        Assert.Equal(10100, outp.Equity[5], 9);
        // candle 5's close (101) triggers a cross-down exit before EndOfData can
        Assert.Equal(ExitReasons.Signal, outp.Trades[^1].Reason);
        Assert.Equal(101, outp.Trades[^1].Exit, 9);
    }

    /* ── D14: win = net > 0 (strict) — zero-P/L trade is a break-even, not a win ── */
    [Fact]
    public void Win_Is_Strictly_Positive_Net()
    {
        var trades = new List<Trade>
        {
            new() { No = 1, Gross = 5, Fees = 5, Net = 0 },     // break-even
            new() { No = 2, Gross = 10, Fees = 0, Net = 10 },   // win
            new() { No = 3, Gross = -4, Fees = 0, Net = -4 },   // loss
        };
        var m = MetricsCalculator.Compute(trades, new[] { 10000.0, 10006 }, 10000);
        Assert.Equal(1, m.Wins);
        Assert.Equal(1, m.Losses);
        Assert.Equal(1, m.BreakEven);
        Assert.Equal(100.0 / 3, m.WinRate, 9);          // D14: wins / total (3 trades incl. break-even)
        Assert.Equal(10, m.GrossProfit, 9);
        Assert.Equal(4, m.GrossLoss, 9);
        Assert.Equal(2.5, m.Pf!, 9);
    }

    /* ── determinism: same inputs → identical trades ── */
    [Fact]
    public void Engine_Is_Deterministic()
    {
        var candles = DemoData();
        var a = BacktestEngine.Run(candles, Sma(sl: 2, tp: 4), 10000);
        var b = BacktestEngine.Run(candles, Sma(sl: 2, tp: 4), 10000);
        Assert.Equal(a.Equity, b.Equity);
        Assert.Equal(a.Trades.Count, b.Trades.Count);
        for (int i = 0; i < a.Trades.Count; i++)
        {
            Assert.Equal(a.Trades[i].EntryTs, b.Trades[i].EntryTs);
            Assert.Equal(a.Trades[i].Net, b.Trades[i].Net, 9);
            Assert.Equal(a.Trades[i].Reason, b.Trades[i].Reason);
        }
    }

    /* longer deterministic series for soak-testing the engine */
    internal static List<Candle> DemoData()
    {
        var list = new List<Candle>();
        double price = 100;
        for (int i = 0; i < 300; i++)
        {
            double drift = System.Math.Sin(i / 18.0) * 1.5;
            double open = price;
            double close = System.Math.Max(1, open + drift);
            double high = System.Math.Max(open, close) + 0.7;
            double low = System.Math.Min(open, close) - 0.7;
            list.Add(new Candle(
                DateTime.UtcNow.Date.AddMinutes(i).ToString("yyyy-MM-ddTHH:mm:ss"),
                open, high, low, close, 500 + i));
            price = close;
        }
        return list;
    }
}
