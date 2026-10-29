using AlgoTrade.Core.Domain;

namespace AlgoTrade.Core.Engine;

/* ═══════════════════════════════════════════════════════════════
   BacktestEngine — deterministic chronological simulator.

   LOCKED rules (identical to js simulate() — demo and C# produce
   the SAME trades for the SAME candles):

     D7   signal computed from completed candle i (data ≤ i only)
     D8   fills at the OPEN of candle i+1
     D9   per-candle SL/TP via high/low — stop-first when both
          hit in the same candle; gap-aware (exit at min/max of
          open vs level)
     D10  no leverage — entry rejected when qty*fill > cash
     D11  flat fee per fill, charged as 2×fee on exit
     D12  equity marked to the CLOSE of every candle
     D13  open position force-closed at the last close (EndOfData)
     D14  win = netPnL > 0 (strictly)
   ═══════════════════════════════════════════════════════════════ */
public sealed class BacktestEngine
{
    private enum Sig { Enter, Exit }

    public sealed record EngineOutput(List<Trade> Trades, double[] Equity);

    public static EngineOutput Run(IReadOnlyList<Candle> candles, StrategyConfig strat, double initialEquity)
    {
        var sig = ComputeSignals(candles, strat);
        var risk = strat.Risk;
        double fee = risk.FeePerTrade;

        var trades = new List<Trade>();
        var equity = new double[candles.Count];
        double cash = initialEquity;

        bool inPos = false;
        double posEntry = 0, posQty = 0;
        string posEntryTs = "";
        double? posStop = null, posTp = null;

        for (int i = 0; i < candles.Count; i++)
        {
            Candle c = candles[i];

            /* ── manage an open position on this candle (D9 → D13) ── */
            if (inPos)
            {
                string? reason = null;
                double exitPrice = 0;

                if (posStop is double stopP && c.L <= stopP)
                { reason = ExitReasons.StopLoss; exitPrice = System.Math.Min(c.O, stopP); }          // gap-aware
                else if (posTp is double tpP && c.H >= tpP)
                { reason = ExitReasons.TakeProfit; exitPrice = System.Math.Max(c.O, tpP); }

                if (reason is null && sig[i] == Sig.Exit)
                { reason = ExitReasons.Signal; exitPrice = c.C; }                                     // evaluated on close i

                if (reason is null && i == candles.Count - 1)
                { reason = ExitReasons.EndOfData; exitPrice = c.C; }                                  // D13

                if (reason is not null)
                {
                    double gross = (exitPrice - posEntry) * posQty;
                    double fees = fee * 2;                                                            // D11: entry + exit fills
                    double net = gross - fees;
                    cash += net;
                    trades.Add(new Trade
                    {
                        No = trades.Count + 1,
                        Dir = "Long",
                        EntryTs = posEntryTs,
                        Entry = posEntry,
                        ExitTs = c.Ts,
                        Exit = exitPrice,
                        Qty = posQty,
                        Gross = gross,
                        Fees = fees,
                        Net = net,
                        Reason = reason,
                    });
                    inPos = false; posStop = null; posTp = null;
                }
            }

            /* ── a signal on candle i fills at candle i+1's open (D8, D10) ── */
            if (!inPos && sig[i] == Sig.Enter && i < candles.Count - 1 && !string.IsNullOrEmpty(risk.Sizing))
            {
                double fill = candles[i + 1].O;
                double qty = risk.Sizing == "percent"
                    ? System.Math.Round(cash * risk.PercentOfEquity / 100.0 / fill, 6, MidpointRounding.AwayFromZero)
                    : risk.FixedUnits;

                if (qty > 0 && qty * fill <= cash)
                {
                    inPos = true;
                    posEntry = fill;
                    posEntryTs = candles[i + 1].Ts;
                    posQty = qty;
                    posStop = risk.SlPct is double sl ? fill * (1 - sl / 100.0) : null;
                    posTp = risk.TpPct is double tp ? fill * (1 + tp / 100.0) : null;
                }
            }

            /* ── D12: mark to the close of every candle ── */
            equity[i] = cash + (inPos ? (c.C - posEntry) * posQty : 0);
        }

        return new EngineOutput(trades, equity);
    }

    /* ── signal generation (D7): computed on candle i using data ≤ i ── */
    private static Sig?[] ComputeSignals(IReadOnlyList<Candle> candles, StrategyConfig strat)
    {
        int n = candles.Count;
        var sig = new Sig?[n];
        var closes = new double[n];
        for (int i = 0; i < n; i++) closes[i] = candles[i].C;

        if (strat.Type == "sma")
        {
            int fast = strat.Int("fast", 10), slow = strat.Int("slow", 30);
            var f = Indicators.Sma(closes, fast);
            var s = Indicators.Sma(closes, slow);
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || f[i] is null || s[i] is null || f[i - 1] is null || s[i - 1] is null) continue;
                bool crossUp = f[i - 1] <= s[i - 1] && f[i] > s[i];
                bool crossDn = f[i - 1] >= s[i - 1] && f[i] < s[i];
                sig[i] = crossUp ? Sig.Enter : crossDn ? Sig.Exit : null;
            }
        }
        else if (strat.Type == "donchian")
        {
            int entry = strat.Int("entry", 20), exitB = strat.Int("exit", 10);
            for (int i = 0; i < n; i++)
            {
                if (i < entry || i == 0) continue;
                double hh = Indicators.HighestHigh(candles, i, entry);
                double ll = Indicators.LowestLow(candles, i, exitB);
                sig[i] = candles[i].C > hh ? Sig.Enter : candles[i].C < ll ? Sig.Exit : null;
            }
        }
        else if (strat.Type == "rsi")
        {
            int period = strat.Int("period", 14), os = strat.Int("os", 28), ob = strat.Int("ob", 72);
            var r = Indicators.Rsi(closes, period);
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || r[i] is null || r[i - 1] is null) continue;
                sig[i] = (r[i - 1] <= os && r[i] > os) ? Sig.Enter
                       : (r[i - 1] >= ob && r[i] < ob) ? Sig.Exit : null;
            }
        }
        /* unknown type ⇒ all null ⇒ zero trades (a valid empty result) */
        return sig;
    }
}
