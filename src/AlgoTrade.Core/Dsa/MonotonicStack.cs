namespace AlgoTrade.Core.Dsa;


public static class MonotonicStack
{
    public static int[] NextGreater(double[] v, OpCounter? ops = null)
        => NextExtreme(v, greater: true, ops);

    public static int[] NextSmaller(double[] v, OpCounter? ops = null)
        => NextExtreme(v, greater: false, ops);

    private static int[] NextExtreme(double[] v, bool greater, OpCounter? ops)
    {
        var op = ops ?? new OpCounter();
        int n = v.Length;
        var res = new int[n];
        for (int i = 0; i < n; i++) res[i] = -1;   // "none" sentinel — mirrors js & the naive baseline
        var stack = new int[n];
        int top = -1;
        for (int i = 0; i < n; i++)
        {
            while (top >= 0)
            {
                op.CountComparison();
                bool pop = greater ? v[stack[top]] < v[i] : v[stack[top]] > v[i];
                if (!pop) break;
                res[stack[top--]] = i;
                op.CountMove();
            }
            stack[++top] = i;
            op.CountMove();
        }
        return res;
    }

    /// <summary>O(n²) naive baseline: scan forward until the answer is found. Returns comparison count via ops.</summary>
    public static int[] NextGreaterNaive(double[] v, OpCounter ops)
    {
        int n = v.Length;
        var res = new int[n];
        for (int i = 0; i < n; i++)
        {
            res[i] = -1;
            for (int j = i + 1; j < n; j++)
            {
                ops.CountComparison();
                if (v[j] > v[i]) { res[i] = j; break; }
            }
        }
        return res;
    }
}
