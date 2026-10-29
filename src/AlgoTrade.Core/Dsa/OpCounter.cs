namespace AlgoTrade.Core.Dsa;

/* ═══════════════════════════════════════════════════════════════
   OpCounter — counts primitive operations so every structure can
   EVIDENCE its Big-O instead of just claiming it (Blueprint §7).

   Comparisons — element-vs-element or key-vs-key comparisons
   Moves       — writes/swaps/copies of elements
   Probes      — slot/queue/node inspections (hash, tree, deque)
   ═══════════════════════════════════════════════════════════════ */
public sealed class OpCounter
{
    public long Comparisons { get; private set; }
    public long Moves { get; private set; }
    public long Probes { get; private set; }

    public long Total => Comparisons + Moves + Probes;

    public void CountComparison(int n = 1) => Comparisons += n;
    public void CountMove(int n = 1) => Moves += n;
    public void CountProbe(int n = 1) => Probes += n;

    public void Reset() { Comparisons = 0; Moves = 0; Probes = 0; }

    public OpSnapshot Snapshot() => new(Comparisons, Moves, Probes);

    public readonly record struct OpSnapshot(long Comparisons, long Moves, long Probes)
    {
        public long Total => Comparisons + Moves + Probes;
    }
}
