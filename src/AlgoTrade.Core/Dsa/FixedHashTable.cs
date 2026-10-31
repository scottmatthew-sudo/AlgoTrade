using System.Numerics;

namespace AlgoTrade.Core.Dsa;

/* ═══════════════════════════════════════════════════════════════
   FixedHashTable — open addressing (linear probing), power-of-two
   capacity, FNV-style 64-bit mix. long key → int value.

   Used by: CandleSeries timestamp→index lookups (O(1) average),
   and benchmarked against a linear-scan baseline on /benchmark.

   Capacity is FIXED at construction (no rehash): sized up-front
   to hold `capacity` entries at ≤ 0.75 load factor. Inserting
   beyond that throws — deterministic and honest.
   ═══════════════════════════════════════════════════════════════ */
public sealed class FixedHashTable
{
    private readonly long[] _keys;
    private readonly int[] _values;
    private readonly byte[] _state;      // 0 empty, 1 used
    private readonly int _mask;
    private readonly OpCounter _ops;
    public int Count { get; private set; }
    public int Capacity => _keys.Length;

    public FixedHashTable(int expectedEntries, OpCounter? ops = null)
    {
        int need = System.Math.Max(16, (int)(expectedEntries / 0.75) + 1);
        int size = (int)BitOperations.RoundUpToPowerOf2((uint)need);
        _keys = new long[size];
        _values = new int[size];
        _state = new byte[size];
        _mask = size - 1;
        _ops = ops ?? new OpCounter();
    }

    private static ulong Mix(ulong x)
    {
        x ^= x >> 33; x *= 0xff51afd7ed558ccdUL;
        x ^= x >> 33; x *= 0xc4ceb9fe1a85ec53UL;
        x ^= x >> 33;
        return x;
    }

    private int Slot(long key)
    {
        ulong h = Mix((ulong)key);
        return (int)(h & (ulong)_mask);
    }

    /// <summary>Insert or overwrite. Returns true when a new slot was claimed.</summary>
    public bool Put(long key, int value)
    {
        if (Count >= Capacity)
            throw new InvalidOperationException("Hash table full — construct with a larger capacity.");
        int i = Slot(key);
        while (true)
        {
            _ops.CountProbe();
            if (_state[i] == 0)
            {
                _state[i] = 1; _keys[i] = key; _values[i] = value;
                Count++; _ops.CountMove();
                return true;
            }
            if (_keys[i] == key) { _values[i] = value; _ops.CountComparison(); return false; }
            _ops.CountComparison();
            i = (i + 1) & _mask;
        }
    }

    public bool TryGet(long key, out int value)
    {
        int i = Slot(key);
        while (true)
        {
            _ops.CountProbe();
            if (_state[i] == 0) { value = 0; return false; }
            _ops.CountComparison();
            if (_keys[i] == key) { value = _values[i]; return true; }
            i = (i + 1) & _mask;
        }
    }
}
