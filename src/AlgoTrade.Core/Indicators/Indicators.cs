using AlgoTrade.Core.Domain;

namespace AlgoTrade.Core;

/* NOTE: this class intentionally lives in the ROOT namespace AlgoTrade.Core.
   A class named "Indicators" inside a namespace "…Core.Indicators" collides
   with its own namespace at call sites (C# resolves the child namespace
   first → CS0234). Root placement makes Indicators.Sma(...) resolve cleanly. */


public static class Indicators
{
    /// <summary>Simple moving average; null during warm-up (mirrors js sma()).</summary>
    public static double?[] Sma(double[] values, int period)
    {
        var outArr = new double?[values.Length];
        double sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
            if (i >= period) sum -= values[i - period];
            if (i >= period - 1) outArr[i] = sum / period;
        }
        return outArr;
    }

    /// <summary>Wilder RSI; null until index == period (mirrors js rsi()).</summary>
    public static double?[] Rsi(double[] closes, int period)
    {
        var outArr = new double?[closes.Length];
        double g = 0, l = 0;
        for (int i = 1; i < closes.Length; i++)
        {
            double d = closes[i] - closes[i - 1];
            double up = System.Math.Max(d, 0), dn = System.Math.Max(-d, 0);
            if (i <= period)
            {
                g += up; l += dn;
                if (i == period)
                {
                    g /= period; l /= period;
                    outArr[i] = l == 0 ? 100 : 100 - 100 / (1 + g / l);
                }
            }
            else
            {
                g = (g * (period - 1) + up) / period;
                l = (l * (period - 1) + dn) / period;
                outArr[i] = l == 0 ? 100 : 100 - 100 / (1 + g / l);
            }
        }
        return outArr;
    }

    /// <summary>Highest high of candles[i-n .. i-1] (excludes i) — mirrors js highestHigh().</summary>
    public static double HighestHigh(IReadOnlyList<Candle> cs, int i, int n)
    {
        double m = double.NegativeInfinity;
        for (int j = System.Math.Max(0, i - n); j < i; j++) m = System.Math.Max(m, cs[j].H);
        return m;
    }

    /// <summary>Lowest low of candles[i-n .. i-1] (excludes i) — mirrors js lowestLow().</summary>
    public static double LowestLow(IReadOnlyList<Candle> cs, int i, int n)
    {
        double m = double.PositiveInfinity;
        for (int j = System.Math.Max(0, i - n); j < i; j++) m = System.Math.Min(m, cs[j].L);
        return m;
    }
}
