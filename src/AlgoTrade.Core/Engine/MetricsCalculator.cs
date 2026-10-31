using AlgoTrade.Core.Domain;

namespace AlgoTrade.Core.Engine;


public static class MetricsCalculator
{
    public static PerformanceMetrics Compute(IReadOnlyList<Trade> trades, IReadOnlyList<double> equity, double initialEquity)
    {
        int done = trades.Count;
        int wins = 0, losses = 0;
        double gp = 0, gl = 0;
        foreach (var t in trades)
        {
            if (t.Net > 0) { wins++; gp += t.Gross; }
            else if (t.Net < 0) { losses++; gl += System.Math.Abs(t.Gross); }
        }
        int be = done - wins - losses;

        double peak = double.NegativeInfinity, maxDd = 0, maxDdPct = 0;
        foreach (var e in equity)
        {
            if (e > peak) peak = e;
            double dd = peak - e;
            if (dd > maxDd) { maxDd = dd; maxDdPct = peak != 0 ? dd / peak * 100.0 : 0; }
        }

        double final = equity.Count > 0 ? equity[^1] : initialEquity;

        return new PerformanceMetrics
        {
            Total = done,
            Wins = wins,
            Losses = losses,
            BreakEven = be,
            Initial = initialEquity,
            Final = final,
            Net = final - initialEquity,
            GrossProfit = gp,
            GrossLoss = gl,
            WinRate = done > 0 ? wins * 100.0 / done : 0,
            Pf = gl > 0 ? gp / gl : null,
            MaxDd = maxDd,
            MaxDdPct = maxDdPct,
        };
    }
}
