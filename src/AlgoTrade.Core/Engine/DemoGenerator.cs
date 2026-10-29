using AlgoTrade.Core.Domain;

namespace AlgoTrade.Core.Engine;

/* ═══════════════════════════════════════════════════════════════
   DemoGenerator — server-side port of js genCandles() (GBM with
   volatility clustering + mulberry32 seeded RNG), powering
   POST /datasets/demo (DOGEUSDT · 5m seed 88 n 1200).

   The RNG is bit-exact with the browser's mulberry32 (uint32
   arithmetic) so demo datasets line up across modes; timestamps
   anchor to "now" like the JS version.
   ═══════════════════════════════════════════════════════════════ */
public static class DemoGenerator
{
    public sealed record DemoSpec(string Symbol, int Seed, int N, int TfMinutes, double EndPrice, double VolPct);

    public static readonly DemoSpec Default = new("DOGEUSDT", 88, 1200, 5, 0.32, 0.008);

    public static List<Candle> GenCandles(DemoSpec spec, double driftPct = 0.00002, long? nowMs = null)
    {
        uint a = (uint)spec.Seed;
        double Rng()
        {
            a = unchecked(a + 0x6D2B79F5u);
            uint t = a ^ (a >> 15);
            t = unchecked(t * (1u | t));
            uint u = t ^ (t >> 7);
            t = unchecked((t + u * (61u | t)) ^ t);
            return t / 4294967296.0;
        }
        double Gauss()
        {
            double u = 0, v = 0;
            while (u == 0) u = Rng();
            while (v == 0) v = Rng();
            return System.Math.Sqrt(-2 * System.Math.Log(u)) * System.Math.Cos(2 * System.Math.PI * v);
        }

        long tfMs = spec.TfMinutes * 60000L;
        long now = nowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long end = now / tfMs * tfMs;
        long start = end - (spec.N - 1) * tfMs;

        var outList = new List<Candle>(spec.N);
        double price = spec.EndPrice * System.Math.Exp(-driftPct * spec.N);

        for (int i = 0; i < spec.N; i++)
        {
            string ts = DateTimeOffset.FromUnixTimeMilliseconds(start + i * tfMs)
                               .UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss");

            double regime = 0.65 + 0.7 * (0.5 + 0.5 * System.Math.Sin(i / 60.0 + spec.Seed));
            double shock = Rng() < 0.015 ? 2.2 + Rng() * 2 : 1;
            double sigma = spec.VolPct * regime * shock;
            double ret = driftPct + sigma * Gauss();

            double open = price;
            double close = open * System.Math.Exp(ret);
            double wick = System.Math.Abs(sigma) * open * (0.4 + Rng() * 1.1);
            double high = System.Math.Max(open, close) + wick * Rng();
            double low = System.Math.Min(open, close) - wick * Rng();
            double vol = System.Math.Round((400 + Rng() * 900) * (1 + System.Math.Abs(ret) / spec.VolPct * 0.6) * (spec.TfMinutes / 60.0), MidpointRounding.AwayFromZero);

            outList.Add(new Candle(ts,
                R6(open), R6(high), R6(System.Math.Max(low, 0.01)), R6(close), vol));
            price = close;
        }
        return outList;
    }

    private static double R6(double x) => System.Math.Round(x, 6, MidpointRounding.AwayFromZero);
}
