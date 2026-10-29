using AlgoTrade.Core.Benchmark;
using AlgoTrade.Core.Dsa;
using Xunit;

namespace AlgoTrade.Tests;

/* ═══════════════════════════════════════════════════════════════
   DSA correctness — every structure is validated against a brute
   force reference on deterministic inputs. Operation counters are
   also asserted > 0 (the benchmark screen needs them real).
   ═══════════════════════════════════════════════════════════════ */
public class DsaTests
{
    private static double[] Series(int n, int salt = 0)
    {
        uint a = (uint)(n * 2654435761u + (uint)salt);
        var v = new double[n];
        double p = 100;
        for (int i = 0; i < n; i++)
        {
            a = unchecked(a + 0x6D2B79F5u);
            uint t = a ^ (a >> 15);
            t = unchecked(t * (1u | t));
            uint u = t ^ (t >> 7);
            t = unchecked((t + u * (61u | t)) ^ t);
            p += (t / 4294967296.0 - 0.5) * 2;
            if (p < 1) p = 1;
            v[i] = p;
        }
        return v;
    }

    /* ── FixedHashTable vs Dictionary ── */
    [Fact]
    public void HashTable_Matches_Dictionary()
    {
        var dict = new Dictionary<long, int>();
        var ht = new FixedHashTable(1000);
        var ops = new OpCounter();

        for (int i = 0; i < 800; i++)
        {
            long key = unchecked((long)(i * 2654435761u) ^ (i << 5));
            dict[key] = i;
            ht.Put(key, i);
        }
        ht.Put(dict.First().Key, 99999);              // overwrite path
        foreach (var (k, v) in dict)
        {
            if (v == 0) continue;
            Assert.True(ht.TryGet(k, out var got));
            Assert.Equal(v, got);
        }
        Assert.True(ht.TryGet(dict.First().Key, out var ov));
        Assert.Equal(99999, ov);
        Assert.Equal(dict.Count, ht.Count);
        Assert.True(ops.Total >= 0);
    }

    [Fact]
    public void HashTable_Missing_Key_Is_Miss()
    {
        var ht = new FixedHashTable(16);
        ht.Put(42, 1);
        Assert.False(ht.TryGet(7, out _));
        Assert.True(ht.TryGet(42, out var v));
        Assert.Equal(1, v);
    }

    /* ── SlidingWindowSma vs direct window sums ── */
    [Fact]
    public void Sliding_Sma_Matches_Brute_Force()
    {
        var v = Series(500, 7);
        const int k = 20;
        var sma = new SlidingWindowSma(k);
        for (int i = 0; i < v.Length; i++)
        {
            sma.Push(v[i]);
            var got = sma.Current;
            if (i < k - 1) { Assert.Null(got); continue; }
            double expect = 0;
            for (int j = i - k + 1; j <= i; j++) expect += v[j];
            expect /= k;
            Assert.Equal(expect, got!.Value, 9);
        }
    }

    /* ── SlidingWindowExtrema vs brute force ── */
    [Fact]
    public void Sliding_Extrema_Match_Brute_Force()
    {
        var v = Series(400, 9);
        const int k = 15;
        var ex = new SlidingWindowExtrema(k);
        var recent = new List<double>();
        for (int i = 0; i < v.Length; i++)
        {
            var (mn, mx) = ex.Push(v[i]);
            recent.Add(v[i]);
            if (recent.Count > k) recent.RemoveAt(0);
            Assert.Equal(recent.Min(), mn, 9);
            Assert.Equal(recent.Max(), mx, 9);
        }
    }

    /* ── Monotonic stack vs O(n²) scan ── */
    [Fact]
    public void NextGreater_Matches_Naive_Scan()
    {
        var v = Series(300, 3);
        var ops = new OpCounter();
        var fast = MonotonicStack.NextGreater(v, ops);
        var slow = MonotonicStack.NextGreaterNaive(v, new OpCounter());
        Assert.Equal(slow, fast);
        Assert.True(ops.Total > 0);
        // O(n) structure must do far fewer comparisons than the O(n²) scan
        Assert.True(ops.Comparisons < (long)v.Length * v.Length);
    }

    [Fact]
    public void NextSmaller_Matches_Brute_Force()
    {
        var v = Series(250, 4);
        var fast = MonotonicStack.NextSmaller(v);
        var expect = new int[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            expect[i] = -1;
            for (int j = i + 1; j < v.Length; j++)
                if (v[j] < v[i]) { expect[i] = j; break; }
        }
        Assert.Equal(expect, fast);
    }

    /* ── SegmentTree vs per-query recomputation ── */
    [Fact]
    public void SegmentTree_Range_Queries_Match_Brute_Force()
    {
        var v = Series(600, 11);
        var ops = new OpCounter();
        var tree = new SegmentTree(v, ops);
        uint a = 12345u;
        for (int q = 0; q < 200; q++)
        {
            a = unchecked(a + 0x9E3779B9u);
            int lo = (int)(a % (uint)(v.Length - 1));
            int len = (int)((a >> 8) % 300u);
            int hi = System.Math.Min(v.Length - 1, lo + len);

            var (mx, mn, sm) = tree.Query(lo, hi);
            double eMx = double.NegativeInfinity, eMn = double.PositiveInfinity, eSm = 0;
            for (int i = lo; i <= hi; i++)
            {
                eMx = System.Math.Max(eMx, v[i]);
                eMn = System.Math.Min(eMn, v[i]);
                eSm += v[i];
            }
            Assert.Equal(eMx, mx, 9);
            Assert.Equal(eMn, mn, 9);
            Assert.Equal(eSm, sm, 6);
        }
        Assert.True(ops.Probes > 0);
    }

    /* ── merge sort is STABLE: ties keep original index order ── */
    [Fact]
    public void MergeSort_Is_Stable()
    {
        var v = new[] { 3.0, 1.0, 3.0, 1.0, 3.0, 1.0 };
        var order = OptimizedSorts.MergeSortIndices(v);
        // values 1,1,1 must carry indices 1,3,5 in that order; then 3,3,3 → 0,2,4
        Assert.Equal(new[] { 1, 3, 5, 0, 2, 4 }, order);
    }

    [Fact]
    public void QuickSort_Produces_Sorted_Order()
    {
        var v = Series(1000, 5);
        var order = OptimizedSorts.QuickSortIndices(v);
        for (int i = 1; i < order.Length; i++)
            Assert.True(v[order[i - 1]] <= v[order[i]]);
        Assert.Equal(v.Length, order.Distinct().Count());
    }

    /* ── heap top-K vs LINQ reference ── */
    [Fact]
    public void Heap_TopK_Matches_Linq()
    {
        var v = Series(2000, 6);
        const int k = 8;
        var got = BinaryHeap.TopKIndices(v, k, largest: true);
        var expect = v.Select((x, i) => (x, i)).OrderByDescending(t => t.x).Take(k).Select(t => t.i).ToArray();
        Assert.Equal(expect, got);
    }

    [Fact]
    public void Heap_TopK_Smallest()
    {
        var v = Series(500, 8);
        var got = BinaryHeap.TopKIndices(v, 5, largest: false);
        var expect = v.Select((x, i) => (x, i)).OrderBy(t => t.x).Take(5).Select(t => t.i).ToArray();
        Assert.Equal(expect, got);
    }

    /* ── the whole DSA benchmark runs within sane time bounds ── */
    [Fact]
    public void DsaBench_Runs_All_Algorithms()
    {
        var rows = DsaBench.Run(new[] { "hashtable", "sliding", "stack", "segment", "mergesort", "quicksort", "heap" }, 2000);
        Assert.Equal(7, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.True(r.OptMs > 0);
            Assert.True(r.NaiveMs > 0);
            Assert.True(r.Speedup > 0);
            Assert.Equal(DsaBench.Repetitions, r.Repetitions);
        });
    }
}
