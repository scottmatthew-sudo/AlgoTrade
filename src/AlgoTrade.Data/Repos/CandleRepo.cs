using AlgoTrade.Core.Domain;
using AlgoTrade.Data.Turso;

namespace AlgoTrade.Data.Repos;


public sealed class CandleRepo
{
    private readonly TursoClient _db;
    public CandleRepo(TursoClient db) { _db = db; }

    public const int DetailCap = 20000;   // safety cap for the full-detail endpoint
    private const int ChunkSize = 400;

    private const string UpsertSql =
        """
        INSERT INTO Candle (DatasetId, Ts, OpenI, HighI, LowI, CloseI, VolumeI)
        VALUES (?,?,?,?,?,?,?)
        ON CONFLICT(DatasetId, Ts) DO UPDATE SET
          OpenI=excluded.OpenI, HighI=excluded.HighI, LowI=excluded.LowI,
          CloseI=excluded.CloseI, VolumeI=excluded.VolumeI
        """;

    public async Task BulkUpsertAsync(long datasetId, IReadOnlyList<Candle> candles)
    {
        for (int offset = 0; offset < candles.Count; offset += ChunkSize)
        {
            int take = System.Math.Min(ChunkSize, candles.Count - offset);
            var stmts = new List<TursoStmt>(take);
            for (int i = offset; i < offset + take; i++)
            {
                var c = candles[i];
                stmts.Add(new TursoStmt(UpsertSql,
                    datasetId, c.Ts, Scaling.ToI(c.O), Scaling.ToI(c.H), Scaling.ToI(c.L),
                    Scaling.ToI(c.C), Scaling.ToI(c.V)));
            }
            var results = await _db.ExecuteAsync(stmts);
            foreach (var r in results) r.EnsureOk();
        }
    }

    /// <summary>Full chronological load (capped) — dataset detail, backtests, range tools.</summary>
    public async Task<List<Candle>> LoadAllAsync(long datasetId, int cap = DetailCap)
    {
        var r = await _db.OneAsync(
            "SELECT Ts, OpenI, HighI, LowI, CloseI, VolumeI FROM Candle " +
            "WHERE DatasetId = ? ORDER BY Ts LIMIT ?", datasetId, (long)cap);
        r.EnsureOk();
        var list = new List<Candle>(r.RowCount);
        for (int i = 0; i < r.RowCount; i++)
        {
            list.Add(new Candle(
                r.S(i, 0),
                Scaling.FromI(r.L(i, 1)),
                Scaling.FromI(r.L(i, 2)),
                Scaling.FromI(r.L(i, 3)),
                Scaling.FromI(r.L(i, 4)),
                Scaling.FromI(r.L(i, 5))));
        }
        return list;
    }

    /// <summary>One page for the candle table: ORDER BY Ts, OFFSET pagination.</summary>
    public async Task<(List<Candle> Rows, int Total, int Pages)> PageAsync(long datasetId, int page, int size)
    {
        if (page < 1) page = 1;
        if (size < 1) size = 25;

        var results = await _db.ExecuteAsync(new[]
        {
            new TursoStmt("SELECT COUNT(*) AS c FROM Candle WHERE DatasetId = ?", datasetId),
            new TursoStmt(
                "SELECT Ts, OpenI, HighI, LowI, CloseI, VolumeI FROM Candle " +
                "WHERE DatasetId = ? ORDER BY Ts LIMIT ? OFFSET ?", datasetId, (long)size, (long)((page - 1) * size)),
        });
        foreach (var r in results) r.EnsureOk();

        int total = (int)results[0].L(0, 0);
        var rows = new List<Candle>(results[1].RowCount);
        for (int i = 0; i < results[1].RowCount; i++)
        {
            var r = results[1];
            rows.Add(new Candle(
                r.S(i, 0),
                Scaling.FromI(r.L(i, 1)),
                Scaling.FromI(r.L(i, 2)),
                Scaling.FromI(r.L(i, 3)),
                Scaling.FromI(r.L(i, 4)),
                Scaling.FromI(r.L(i, 5))));
        }
        return (rows, total, (int)System.Math.Ceiling(total / (double)size));
    }

    public async Task<int> CountAsync(long datasetId)
    {
        var r = await _db.OneAsync("SELECT COUNT(*) AS c FROM Candle WHERE DatasetId = ?", datasetId);
        r.EnsureOk();
        return (int)r.L(0, 0);
    }

    /// <summary>~40 downsampled closes for the dataset-list sparkline (ROW_NUMBER modulo sampling).</summary>
    public async Task<List<double>> SampleClosesAsync(long datasetId, int points = 40)
    {
        try
        {
            var r = await _db.OneAsync(
                """
                WITH c AS (SELECT CloseI, ROW_NUMBER() OVER (ORDER BY Ts) AS rn FROM Candle WHERE DatasetId = ?),
                     t AS (SELECT COUNT(*) AS total FROM c)
                SELECT CloseI FROM c, t WHERE c.rn % MAX(1, CAST(t.total / ? AS INTEGER)) = 0
                """, datasetId, (long)points);
            r.EnsureOk();
            var list = new List<double>(r.RowCount);
            for (int i = 0; i < r.RowCount; i++) list.Add(Scaling.FromI(r.L(i, 0)));
            return list;
        }
        catch (TursoException)
        {
            return new List<double>();   // spark is decorative — never break the list over it
        }
    }
}
