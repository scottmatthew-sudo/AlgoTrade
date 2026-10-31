namespace AlgoTrade.Core.Dsa;

/* ═══════════════════════════════════════════════════════════════
   SegmentTree — recursive array-backed tree (size 4n) answering
   range MAX / MIN / SUM over candles in O(log n) per query.

   Used by Dataset Details → "Range Statistics" (highest high /
   lowest low / volume / average close between two timestamps) and
   benchmarked against per-query full recomputation.

   Probes count node visits; Comparisons count element comparisons.
   ═══════════════════════════════════════════════════════════════ */
public sealed class SegmentTree
{
    private readonly double[] _max, _min, _sum;
    private readonly int _n;
    private readonly OpCounter _ops;

    public SegmentTree(double[] values, OpCounter? ops = null)
    {
        _n = values.Length;
        _max = new double[System.Math.Max(1, 4 * _n)];
        _min = new double[System.Math.Max(1, 4 * _n)];
        _sum = new double[System.Math.Max(1, 4 * _n)];
        _ops = ops ?? new OpCounter();
        if (_n > 0) Build(values, 1, 0, _n - 1);
    }

    private void Build(double[] v, int node, int lo, int hi)
    {
        _ops.CountProbe();
        if (lo == hi) { _max[node] = v[lo]; _min[node] = v[lo]; _sum[node] = v[lo]; _ops.CountMove(); return; }
        int mid = (lo + hi) >> 1;
        Build(v, 2 * node, lo, mid);
        Build(v, 2 * node + 1, mid + 1, hi);
        _ops.CountComparison(2);
        _max[node] = System.Math.Max(_max[2 * node], _max[2 * node + 1]);
        _min[node] = System.Math.Min(_min[2 * node], _min[2 * node + 1]);
        _sum[node] = _sum[2 * node] + _sum[2 * node + 1];
        _ops.CountMove(3);
    }

    public (double Max, double Min, double Sum) Query(int l, int r)
    {
        if (_n == 0 || l > r) return (double.NegativeInfinity, double.PositiveInfinity, 0);
        l = System.Math.Max(0, l); r = System.Math.Min(_n - 1, r);
        return Q(1, 0, _n - 1, l, r);
    }

    private (double Max, double Min, double Sum) Q(int node, int lo, int hi, int l, int r)
    {
        _ops.CountProbe();
        if (l <= lo && hi <= r) return (_max[node], _min[node], _sum[node]);
        int mid = (lo + hi) >> 1;
        if (r <= mid) return Q(2 * node, lo, mid, l, r);
        if (l > mid) return Q(2 * node + 1, mid + 1, hi, l, r);
        var a = Q(2 * node, lo, mid, l, r);
        var b = Q(2 * node + 1, mid + 1, hi, l, r);
        _ops.CountComparison(2); _ops.CountMove(3);
        return (System.Math.Max(a.Max, b.Max), System.Math.Min(a.Min, b.Min), a.Sum + b.Sum);
    }

    /// <summary>Naive baseline: recompute the whole range for one query. Returns ops too.</summary>
    public static (double Max, double Min, double Sum) QueryNaive(double[] v, int l, int r, OpCounter ops)
    {
        double mx = double.NegativeInfinity, mn = double.PositiveInfinity, sm = 0;
        for (int i = l; i <= r && i < v.Length; i++)
        {
            ops.CountComparison(2); ops.CountMove();
            mx = System.Math.Max(mx, v[i]); mn = System.Math.Min(mn, v[i]); sm += v[i];
        }
        return (mx, mn, sm);
    }
}
