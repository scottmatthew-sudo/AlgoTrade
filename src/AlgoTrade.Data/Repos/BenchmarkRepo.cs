using AlgoTrade.Core.Benchmark;
using AlgoTrade.Data.Turso;

namespace AlgoTrade.Data.Repos;


public sealed class BenchmarkRepo
{
    private readonly TursoClient _db;
    public BenchmarkRepo(TursoClient db) { _db = db; }

    public async Task SaveBatchAsync(string batchId, string machineInfo, IReadOnlyList<BenchRow> rows)
    {
        const string sql =
            """
            INSERT INTO AlgorithmBenchmark
              (BatchId, AlgorithmName, Variant, InputSize, Parameter, Repetitions,
               MedianMs, MinMs, MaxMs, OperationCount, OperationDefinition, MachineInfo)
            VALUES (?,?,?,?,?,?,?,?,?,?,?,?)
            """;

        var stmts = new List<TursoStmt>();
        foreach (var row in rows)
        {
            stmts.Add(new TursoStmt(sql, batchId, row.Label, "Naive", (long)row.Size, "baseline",
                (long)DsaBench.Repetitions, row.NaiveMs, row.MinMs, row.MaxMs, (long)row.NaiveOps,
                $"{row.Unit} — {row.OperationDefinition}", machineInfo));
            stmts.Add(new TursoStmt(sql, batchId, row.Label, "Optimized", (long)row.Size, "hand-built",
                (long)DsaBench.Repetitions, row.OptMs, row.MinMs, row.MaxMs, (long)row.OptOps,
                $"{row.Unit} — {row.OperationDefinition}", machineInfo));
        }

        const int chunk = 60;
        for (int offset = 0; offset < stmts.Count; offset += chunk)
        {
            var results = await _db.ExecuteAsync(stmts.Skip(offset).Take(chunk).ToList());
            foreach (var r in results) r.EnsureOk();
        }
    }
}
