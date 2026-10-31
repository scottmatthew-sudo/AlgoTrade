namespace AlgoTrade.Core.Dsa;

public sealed class BinaryHeap
{
    private double[] _val;
    private int[] _idx;
    public int Count { get; private set; }
    private readonly OpCounter _ops;

    public BinaryHeap(int capacity, OpCounter? ops = null)
    {
        int cap = System.Math.Max(4, capacity);
        _val = new double[cap];
        _idx = new int[cap];
        _ops = ops ?? new OpCounter();
    }

    public (double Val, int Idx) Peek() => (_val[0], _idx[0]);

    public void Push(double val, int idx)
    {
        if (Count == _val.Length) Grow();
        int i = Count++;
        _val[i] = val; _idx[i] = idx; _ops.CountMove();
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            _ops.CountComparison();
            if (_val[p] <= _val[i]) break;
            (_val[p], _val[i]) = (_val[i], _val[p]);
            (_idx[p], _idx[i]) = (_idx[i], _idx[p]);
            _ops.CountMove(2);
            i = p;
        }
    }

    public (double Val, int Idx) Pop()
    {
        var root = (_val[0], _idx[0]);
        Count--;
        if (Count > 0)
        {
            _val[0] = _val[Count]; _idx[0] = _idx[Count]; _ops.CountMove();
            SiftDown(0);
        }
        return root;
    }

    private void SiftDown(int i)
    {
        while (true)
        {
            int l = 2 * i + 1, r = l + 1, m = i;
            if (l < Count) { _ops.CountComparison(); if (_val[l] < _val[m]) m = l; }
            if (r < Count) { _ops.CountComparison(); if (_val[r] < _val[m]) m = r; }
            if (m == i) break;
            (_val[m], _val[i]) = (_val[i], _val[m]);
            (_idx[m], _idx[i]) = (_idx[i], _idx[m]);
            _ops.CountMove(2);
            i = m;
        }
    }

    private void Grow()
    {
        int cap = _val.Length * 2;
        var nv = new double[cap]; var ni = new int[cap];
        Array.Copy(_val, nv, _val.Length);
        Array.Copy(_idx, ni, _idx.Length);
        _val = nv; _idx = ni;
    }

    /// <summary>
    /// Best-k indices by value. largest=true ⇒ the k largest, returned
    /// best-first (descending). Values tie-break by lower index first
    /// through the caller-provided tie handling (strict comparisons).
    /// </summary>
    public static int[] TopKIndices(double[] values, int k, bool largest, OpCounter? ops = null)
    {
        var op = ops ?? new OpCounter();
        k = System.Math.Min(k, values.Length);
        var h = new BinaryHeap(System.Math.Max(1, k), op);
        for (int i = 0; i < values.Length; i++)
        {
            if (h.Count < k) { h.Push(values[i], i); op.CountComparison(); }
            else
            {
                op.CountComparison();
                var root = h.Peek();
                bool better = largest ? values[i] > root.Val : values[i] < root.Val;
                if (better) { h.Pop(); h.Push(values[i], i); }
            }
        }
        var res = new int[k];
        for (int i = k - 1; i >= 0; i--) res[i] = h.Pop().Idx;
        return res;
    }
}
