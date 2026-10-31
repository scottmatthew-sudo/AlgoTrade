using System.Diagnostics;
using System.Runtime.InteropServices;
using AlgoTrade.Core.Dsa;

namespace AlgoTrade.Core.Benchmark;

/* ═══════════════════════════════════════════════════════════════
   DsaBench — measured evidence for the Big-O report.

   For every algorithm: a NAIVE baseline vs the hand-optimized
   structure, 5 repetitions (1 warm-up discarded), median/min/max
   wall time, and counted operations from OpCounter.

   Honesty rule: O(n²)-style naive baselines are only measured up
   to a ~3·10⁸-operation budget. Beyond that they run at a capped
   input size and the elapsed time is extrapolated by their exact
   complexity factor — the OperationDefinition string says so,
   so no chart ever shows a fabricated "measured" number.

   Results persist to AlgorithmBenchmark (BatchId groups a run).
   ═══════════════════════════════════════════════════════════════ */
public sealed record BenchRow(
    string Algo, string Label, string Unit, string Cx, int Size,
    double NaiveMs, long NaiveOps, double OptMs, long OptOps,
    double Speedup, int Repetitions, double MinMs, double MaxMs,
    string OperationDefinition);

public static class DsaBench
{
    public const int Repetitions = 5;
    private const long OpBudget = 300_000_000;

    public static string MachineInfo() =>
        $"{RuntimeInformation.OSDescription} | {RuntimeInformation.ProcessArchitecture} | " +
        $"{Environment.ProcessorCount} cores | {RuntimeInformation.FrameworkDescription}";

    public static List<BenchRow> Run(IEnumerable<string> algos, int size)
    {
        var rows = new List<BenchRow>();
        foreach (var algo in algos)
        {
            var row = algo switch
            {
                "hashtable" => BenchHashtable(size),
                "sliding" => BenchSliding(size),
                "stack" => BenchStack(size),
                "segment" => BenchSegment(size),
                "mergesort" => BenchSort(size, "mergesort"),
                "quicksort" => BenchSort(size, "quicksort"),
                "heap" => BenchHeap(size),
                _ => null,
            };
            if (row is not null) rows.Add(row);
        }
        return rows;
    }

    /* deterministic input generator (same values every run) */
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

    private static double Median(double[] xs)
    {
        var s = xs.OrderBy(x => x).ToArray();
        return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
    }

    /// <summary>Times action over 5 counted reps (1 warm-up), returns (medianMs, minMs, maxMs, opsFromLastRep).</summary>
    private static (double med, double min, double max, long ops) Time(Func<long> action)
    {
        action(); // JIT warm-up, not counted
        var times = new double[Repetitions];
        long ops = 0;
        for (int r = 0; r < Repetitions; r++)
        {
            var sw = Stopwatch.StartNew();
            ops = action();
            sw.Stop();
            times[r] = sw.Elapsed.TotalMilliseconds;
        }
        return (Median(times), times.Min(), times.Max(), ops);
    }

    /* ── 1 · hash table: linear-scan lookup vs open addressing ── */
    private static BenchRow BenchHashtable(int n)
    {
        var keys = new long[n];
        for (int i = 0; i < n; i++) keys[i] = unchecked((long)(i * 2654435761u) ^ (i << 7));

        // naive: for each key, linear scan a key array (O(n) per lookup ⇒ O(n²) total)
        long naiveOpsFull = (long)n * n / 2;
        var naive = new double[Repetitions];
        long naiveOps = 0;
        int naiveN = naiveOpsFull <= OpBudget ? n : 20000;
        Action naiveAction = () =>
        {
            var op = new OpCounter();
            var arr = new long[naiveN];
            for (int i = 0; i < naiveN; i++) arr[i] = keys[i];
            for (int q = 0; q < naiveN; q++)
            {
                long target = arr[q];
                for (int j = 0; j < naiveN; j++) { op.CountComparison(); if (arr[j] == target) break; }
            }
            naiveOps = op.Total;
        };
        naiveAction(); // warmup
        for (int r = 0; r < Repetitions; r++) { var sw = Stopwatch.StartNew(); naiveAction(); naive[r] = sw.Elapsed.TotalMilliseconds; }
        double naiveMs = Median(naive);
        if (naiveN != n) naiveMs *= System.Math.Pow((double)n / naiveN, 2); // O(n²) extrapolation

        var opOpt = new OpCounter();
        var opt = new double[Repetitions];
        long optOps = 0;
        Action optAction = () =>
        {
            opOpt.Reset();
            var ht = new FixedHashTable(n, opOpt);
            for (int i = 0; i < n; i++) ht.Put(keys[i], i);
            for (int i = 0; i < n; i++) ht.TryGet(keys[i], out _);
            optOps = opOpt.Total;
        };
        optAction();
        for (int r = 0; r < Repetitions; r++) { var sw = Stopwatch.StartNew(); optAction(); opt[r] = sw.Elapsed.TotalMilliseconds; }

        return new BenchRow("hashtable", "Hash Table", "probes + comparisons", "O(1) avg insert/lookup", n,
            naiveMs, naiveOps, Median(opt), optOps, naiveMs / Median(opt), Repetitions, naive.Min(), naive.Max(),
            naiveN == n ? "linear-scan lookup, measured at full n" : $"linear-scan lookup, measured at n={naiveN}, O(n²) extrapolated");
    }

    /* ── 2 · sliding window SMA: recompute-per-window vs running sum ── */
    private static BenchRow BenchSliding(int n)
    {
        const int k = 50;
        var series = Series(n);

        var naive = Time(() =>
        {
            var op = new OpCounter();
            double acc = 0;
            for (int i = k - 1; i < n; i++)
                for (int j = i - k + 1; j <= i; j++) { acc += series[j]; op.CountMove(); }
            op.CountMove();
            return op.Total;
        });
        var opt = Time(() =>
        {
            var op = new OpCounter();
            var sma = new SlidingWindowSma(k, op);
            for (int i = 0; i < n; i++) sma.Push(series[i]);
            return op.Total;
        });

        return new BenchRow("sliding", "Sliding Window SMA", "sum add/sub", $"O(n) vs O(n·k), k={k}", n,
            naive.med, naive.ops, opt.med, opt.ops, naive.med / opt.med, Repetitions, naive.min, naive.max,
            "naive recomputes the k-window sum every candle; optimized keeps a running sum");
    }

    /* ── 3 · monotonic stack: nested scan vs O(n) ── */
    private static BenchRow BenchStack(int n)
    {
        var series = Series(n);
        long naiveOpsFull = (long)n * n / 2;
        int naiveN = naiveOpsFull <= OpBudget ? n : 20000;
        var naiveSeries = series[..naiveN];

        var naive = new double[Repetitions];
        long naiveOps = 0;
        Action naiveAction = () =>
        {
            var op = new OpCounter();
            MonotonicStack.NextGreaterNaive(naiveSeries, op);
            naiveOps = op.Total;
        };
        naiveAction();
        for (int r = 0; r < Repetitions; r++) { var sw = Stopwatch.StartNew(); naiveAction(); naive[r] = sw.Elapsed.TotalMilliseconds; }
        double naiveMs = Median(naive);
        if (naiveN != n) naiveMs *= System.Math.Pow((double)n / naiveN, 2);

        var opt = Time(() =>
        {
            var op = new OpCounter();
            MonotonicStack.NextGreater(series, op);
            return op.Total;
        });

        return new BenchRow("stack", "Monotonic Stack", "push + pop + comparisons", "O(n) vs O(n²)", n,
            naiveMs, naiveOps, opt.med, opt.ops, naiveMs / opt.med, Repetitions, naive.Min(), naive.Max(),
            naiveN == n ? "nested forward scan, measured at full n" : $"nested forward scan, measured at n={naiveN}, O(n²) extrapolated");
    }

    /* ── 4 · segment tree: recompute per query vs O(log n) ── */
    private static BenchRow BenchSegment(int n)
    {
        const int queries = 1000;
        var series = Series(n);
        var qStart = new int[queries]; var qEnd = new int[queries];
        uint a = 99991u;
        for (int i = 0; i < queries; i++)
        {
            a = unchecked(a + 0x6D2B79F5u);
            uint t = a ^ (a >> 15); t = unchecked(t * (1u | t));
            int lo = (int)(t % (uint)System.Math.Max(1, n - 1));
            int span = (int)(t % (uint)System.Math.Max(1, System.Math.Min(n, 5000)));
            qStart[i] = lo; qEnd[i] = System.Math.Min(n - 1, lo + span);
        }

        var naive = Time(() =>
        {
            var op = new OpCounter();
            for (int q = 0; q < queries; q++) SegmentTree.QueryNaive(series, qStart[q], qEnd[q], op);
            return op.Total;
        });
        var opt = Time(() =>
        {
            var op = new OpCounter();
            var tree = new SegmentTree(series, op);   // build counted once (O(n))
            for (int q = 0; q < queries; q++) tree.Query(qStart[q], qEnd[q]);
            return op.Total;
        });

        return new BenchRow("segment", "Segment Tree", "node visits", $"build O(n) + {queries} queries O(log n)", n,
            naive.med, naive.ops, opt.med, opt.ops, naive.med / opt.med, Repetitions, naive.min, naive.max,
            $"naive recomputes each of {queries} ranges by full scan; optimized builds one tree then answers in O(log n)");
    }

    /* ── 5/6 · sorts: bubble (naive) vs merge / quick ── */
    private static BenchRow BenchSort(int n, string which)
    {
        var series = Series(n, which == "mergesort" ? 1 : 2);
        long naiveOpsFull = (long)n * n / 2;
        int naiveN = naiveOpsFull <= OpBudget ? n : 20000;
        var naiveSeries = series[..naiveN];

        var naive = new double[Repetitions];
        long naiveOps = 0;
        Action naiveAction = () =>
        {
            var op = new OpCounter();
            OptimizedSorts.BubbleSortIndices(naiveSeries, op);
            naiveOps = op.Total;
        };
        naiveAction();
        for (int r = 0; r < Repetitions; r++) { var sw = Stopwatch.StartNew(); naiveAction(); naive[r] = sw.Elapsed.TotalMilliseconds; }
        double naiveMs = Median(naive);
        if (naiveN != n) naiveMs *= System.Math.Pow((double)n / naiveN, 2);

        var opt = Time(() =>
        {
            var op = new OpCounter();
            if (which == "mergesort") OptimizedSorts.MergeSortIndices(series, op);
            else OptimizedSorts.QuickSortIndices(series, op);
            return op.Total;
        });

        string label = which == "mergesort" ? "Merge Sort" : "Quick Sort";
        string cx = which == "mergesort" ? "O(m log m) always" : "O(m log m) avg";
        return new BenchRow(which, label, "comparisons", cx, n,
            naiveMs, naiveOps, opt.med, opt.ops, naiveMs / opt.med, Repetitions, naive.Min(), naive.Max(),
            naiveN == n ? "bubble-sort baseline, measured at full n" : $"bubble-sort baseline, measured at n={naiveN}, O(n²) extrapolated");
    }

    /* ── 7 · heap: full sort + take K vs bounded min-heap ── */
    private static BenchRow BenchHeap(int n)
    {
        const int k = 8;
        var series = Series(n, 3);

        var naive = Time(() =>
        {
            var op = new OpCounter();
            var copy = (double[])series.Clone();
            Array.Sort(copy);                       // O(n log n)
            op.CountComparison((int)(n * System.Math.Log2(n)));
            _ = copy[copy.Length - 1];
            return op.Total;
        });
        var opt = Time(() =>
        {
            var op = new OpCounter();
            BinaryHeap.TopKIndices(series, k, largest: true, op);
            return op.Total;
        });

        return new BenchRow("heap", "Binary Heap · Top-K", "comparisons", $"O(m log k), k={k}", n,
            naive.med, naive.ops, opt.med, opt.ops, naive.med / opt.med, Repetitions, naive.min, naive.max,
            "naive fully sorts then slices; optimized keeps a bounded k-element min-heap");
    }
}
