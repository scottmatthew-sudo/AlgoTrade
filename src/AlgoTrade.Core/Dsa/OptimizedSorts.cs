namespace AlgoTrade.Core.Dsa;


public static class OptimizedSorts
{
    public static int[] MergeSortIndices(double[] v, OpCounter? ops = null)
    {
        var op = ops ?? new OpCounter();
        var a = new int[v.Length];
        for (int i = 0; i < a.Length; i++) a[i] = i;
        var buf = new int[v.Length];
        MergeRec(v, a, buf, 0, v.Length, op);
        return a;
    }

    private static void MergeRec(double[] v, int[] a, int[] buf, int lo, int hi, OpCounter op)
    {
        if (hi - lo < 2) return;
        int mid = (lo + hi) >> 1;
        MergeRec(v, a, buf, lo, mid, op);
        MergeRec(v, a, buf, mid, hi, op);
        int i = lo, j = mid, k = lo;
        while (i < mid && j < hi)
        {
            op.CountComparison();
            buf[k++] = v[a[j]] < v[a[i]] ? a[j++] : a[i++];   // strict < keeps stability
            op.CountMove();
        }
        while (i < mid) { buf[k++] = a[i++]; op.CountMove(); }
        while (j < hi) { buf[k++] = a[j++]; op.CountMove(); }
        for (int t = lo; t < hi; t++) { a[t] = buf[t]; op.CountMove(); }
    }

    public static int[] QuickSortIndices(double[] v, OpCounter? ops = null)
    {
        var op = ops ?? new OpCounter();
        var a = new int[v.Length];
        for (int i = 0; i < a.Length; i++) a[i] = i;
        QuickRec(v, a, 0, a.Length - 1, op);
        return a;
    }

    private static void QuickRec(double[] v, int[] a, int lo, int hi, OpCounter op)
    {
        while (lo < hi)
        {
            if (hi - lo < 16) { Insertion(v, a, lo, hi, op); return; }
            int p = Partition(v, a, lo, hi, op);
            QuickRec(v, a, lo, p - 1, op);
            lo = p + 1;    // tail-loop the larger side away
        }
    }

    private static int Partition(double[] v, int[] a, int lo, int hi, OpCounter op)
    {
        int mid = lo + (hi - lo) / 2;
        op.CountComparison(3);
        if (v[a[mid]] < v[a[lo]]) (a[mid], a[lo]) = (a[lo], a[mid]);
        if (v[a[hi]] < v[a[lo]]) (a[hi], a[lo]) = (a[lo], a[hi]);
        if (v[a[mid]] < v[a[hi]]) (a[mid], a[hi]) = (a[hi], a[mid]);  // median now at hi
        op.CountMove(3);
        double pivot = v[a[hi]];
        int i = lo - 1;
        for (int j = lo; j < hi; j++)
        {
            op.CountComparison();
            if (v[a[j]] <= pivot) { i++; (a[i], a[j]) = (a[j], a[i]); op.CountMove(); }
        }
        (a[i + 1], a[hi]) = (a[hi], a[i + 1]); op.CountMove();
        return i + 1;
    }

    private static void Insertion(double[] v, int[] a, int lo, int hi, OpCounter op)
    {
        for (int i = lo + 1; i <= hi; i++)
        {
            int key = a[i]; double kv = v[key];
            int j = i - 1;
            while (j >= lo)
            {
                op.CountComparison();
                if (v[a[j]] > kv) { a[j + 1] = a[j]; op.CountMove(); j--; }
                else break;
            }
            a[j + 1] = key; op.CountMove();
        }
    }

    /// <summary>Naive O(n²) baseline used only by /benchmark (small caps + extrapolation).</summary>
    public static int[] BubbleSortIndices(double[] v, OpCounter? ops = null)
    {
        var op = ops ?? new OpCounter();
        var a = new int[v.Length];
        for (int i = 0; i < a.Length; i++) a[i] = i;
        for (int i = 0; i < a.Length - 1; i++)
        {
            bool swapped = false;
            for (int j = 0; j < a.Length - 1 - i; j++)
            {
                op.CountComparison();
                if (v[a[j]] > v[a[j + 1]])
                {
                    (a[j], a[j + 1]) = (a[j + 1], a[j]);
                    op.CountMove(); swapped = true;
                }
            }
            if (!swapped) break;   // already sorted
        }
        return a;
    }
}
