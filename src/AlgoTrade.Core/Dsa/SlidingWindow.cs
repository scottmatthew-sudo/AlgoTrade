namespace AlgoTrade.Core.Dsa;

/* ═══════════════════════════════════════════════════════════════
   SlidingWindowSma — k-period moving average via circular buffer +
   running sum. O(1) per candle: subtract the oldest, add the new.

   Mirrors js sma() semantics: value becomes available at candle
   index (period - 1). Benchmarked against the naive "recompute the
   whole window every step" baseline (O(n·k)) on /benchmark.
   ═══════════════════════════════════════════════════════════════ */
public sealed class SlidingWindowSma
{
    private readonly double[] _buf;
    private readonly int _period;
    private readonly OpCounter _ops;
    private int _head;
    private long _filled;
    private double _sum;

    public SlidingWindowSma(int period, OpCounter? ops = null)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        _period = period;
        _buf = new double[period];
        _ops = ops ?? new OpCounter();
    }

    public long Filled => _filled;

    public void Push(double x)
    {
        if (_filled >= _period) { _sum -= _buf[_head]; _ops.CountMove(); }
        else _filled++;
        _buf[_head] = x; _sum += x; _ops.CountMove();
        _head = (_head + 1) % _period;
    }

    /// <summary>SMA of the current window, or null during warm-up (mirrors JS null).</summary>
    public double? Current => _filled >= _period ? _sum / _period : null;
}

/* ═══════════════════════════════════════════════════════════════
   SlidingWindowExtrema — monotonic-deque min/max over a rolling
   window (each index enters and leaves the deque at most once).
   ═══════════════════════════════════════════════════════════════ */
public sealed class SlidingWindowExtrema
{
    private readonly int _k;
    private readonly double[] _val; private readonly long[] _seq;
    private readonly long[] _minQ, _maxQ;
    private int _minH, _minC, _maxH, _maxC;
    private long _seqCounter;
    private readonly OpCounter _ops;

    public SlidingWindowExtrema(int window, OpCounter? ops = null)
    {
        if (window < 1) throw new ArgumentOutOfRangeException(nameof(window));
        _k = window;
        _val = new double[window]; _seq = new long[window];
        _minQ = new long[window]; _maxQ = new long[window];
        _ops = ops ?? new OpCounter();
    }

    private void PopMinFront() { _minH = (_minH + 1) % _k; _minC--; }
    private void PopMaxFront() { _maxH = (_maxH + 1) % _k; _maxC--; }

    public (double Min, double Max) Push(double x)
    {
        long seq = _seqCounter++;

        /* Evict the element leaving the window FIRST. Writing before
           evicting let the back-insert land on the front slot when the
           ring was full (count == k) — corrupting the deque. Evicting
           first caps the ring at k-1 live entries before the write. */
        while (_maxC > 0 && _seq[_maxQ[_maxH] % _k] <= seq - _k) PopMaxFront();
        while (_minC > 0 && _seq[_minQ[_minH] % _k] <= seq - _k) PopMinFront();

        _val[seq % _k] = x; _seq[seq % _k] = seq; _ops.CountMove();

        // max deque: strictly decreasing values front→back
        while (_maxC > 0)
        {
            long idx = _maxQ[(_maxH + _maxC - 1) % _k];
            _ops.CountComparison();
            if (_val[idx % _k] <= x) { _maxC--; } else break;
        }
        _maxQ[(_maxH + _maxC) % _k] = seq; _maxC++;

        // min deque: strictly increasing values front→back
        while (_minC > 0)
        {
            long idx = _minQ[(_minH + _minC - 1) % _k];
            _ops.CountComparison();
            if (_val[idx % _k] >= x) { _minC--; } else break;
        }
        _minQ[(_minH + _minC) % _k] = seq; _minC++;

        return (_val[_minQ[_minH] % _k], _val[_maxQ[_maxH] % _k]);
    }
}
