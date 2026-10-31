using System.Text.Json;
using AlgoTrade.Core.Domain;
using AlgoTrade.Data.Turso;

namespace AlgoTrade.Data.Repos;

public sealed class DatasetRepo
{
    private readonly TursoClient _db;
    public DatasetRepo(TursoClient db) { _db = db; }

    private const string SelectCols =
        "DatasetId, Name, InstrumentLabel, Timeframe, SourceFileName, ImportedAtUtc, " +
        "RowsRead, RowsAccepted, RowsRejected, FirstTimestamp, LastTimestamp, ValidationIssuesJson";

    public async Task<List<DatasetMeta>> ListAsync()
    {
        var r = await _db.OneAsync($"SELECT {SelectCols} FROM MarketDataset ORDER BY DatasetId DESC");
        r.EnsureOk();
        var list = new List<DatasetMeta>(r.RowCount);
        for (int i = 0; i < r.RowCount; i++) list.Add(Map(r, i));
        return list;
    }

    public async Task<DatasetMeta?> GetAsync(long id)
    {
        var r = await _db.OneAsync($"SELECT {SelectCols} FROM MarketDataset WHERE DatasetId = ?", id);
        r.EnsureOk();
        return r.RowCount == 0 ? null : Map(r, 0);
    }

    public async Task<bool> NameExistsAsync(string name)
    {
        var r = await _db.OneAsync("SELECT COUNT(*) AS c FROM MarketDataset WHERE Name = ?", name);
        r.EnsureOk();
        return r.L(0, 0) > 0;
    }

    /// <summary>Inserts a dataset; UNIQUE name conflicts get a timestamped suffix (browser store has no such limit).</summary>
    public async Task<long> InsertAsync(DatasetMeta meta, List<CsvIssue>? issues)
    {
        string name = meta.Name;
        if (await NameExistsAsync(name))
            name = $"{name} · imported {DateTime.UtcNow:HH:mm:ss}";

        string issuesJson = issues is null ? "[]" : JsonSerializer.Serialize(issues);
        var r = await _db.OneAsync(
            """
            INSERT INTO MarketDataset
              (Name, InstrumentLabel, Timeframe, SourceFileName, RowsRead, RowsAccepted, RowsRejected,
               FirstTimestamp, LastTimestamp, ValidationIssuesJson)
            VALUES (?,?,?,?,?,?,?,?,?,?)
            """,
            name, meta.InstrumentLabel, meta.Timeframe, meta.SourceFileName,
            (long)meta.RowsRead, (long)meta.RowsAccepted, (long)meta.RowsRejected,
            meta.FirstTimestamp, meta.LastTimestamp, issuesJson);
        r.EnsureOk();
        return r.LastInsertRowId ?? 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        // runs block deletion (mirrors db.deleteDataset FK rule)
        var rc = await _db.OneAsync("SELECT COUNT(*) AS c FROM BacktestRun WHERE DatasetId = ?", id);
        rc.EnsureOk();
        if (rc.L(0, 0) > 0) return false;

        var results = await _db.ExecuteAsync(new[]
        {
            new TursoStmt("DELETE FROM Candle WHERE DatasetId = ?", id),
            new TursoStmt("DELETE FROM MarketDataset WHERE DatasetId = ?", id),
        });
        foreach (var r in results) r.EnsureOk();
        return results[1].AffectedRowCount > 0;
    }

    public async Task<int> CountRunsAsync(long datasetId)
    {
        var r = await _db.OneAsync("SELECT COUNT(*) AS c FROM BacktestRun WHERE DatasetId = ?", datasetId);
        r.EnsureOk();
        return (int)r.L(0, 0);
    }

    private static DatasetMeta Map(TursoResult r, int row) => new()
    {
        Id = r.L(row, 0),
        Name = r.S(row, 1),
        InstrumentLabel = r.SN(row, 2),
        Timeframe = r.SN(row, 3),
        SourceFileName = r.SN(row, 4),
        ImportedAtUtc = r.S(row, 5),
        RowsRead = (int)r.L(row, 6),
        RowsAccepted = (int)r.L(row, 7),
        RowsRejected = (int)r.L(row, 8),
        FirstTimestamp = r.SN(row, 9),
        LastTimestamp = r.SN(row, 10),
        ValidationIssues = ParseIssues(r.SN(row, 11)),
    };

    private static List<CsvIssue> ParseIssues(string? json)
        => string.IsNullOrEmpty(json) ? new List<CsvIssue>()
         : (JsonSerializer.Deserialize<List<CsvIssue>>(json) ?? new List<CsvIssue>());
}
public sealed class DatasetMeta
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? InstrumentLabel { get; set; }
    public string? Timeframe { get; set; }
    public string? SourceFileName { get; set; }
    public string ImportedAtUtc { get; set; } = "";
    public int RowsRead { get; set; }
    public int RowsAccepted { get; set; }
    public int RowsRejected { get; set; }
    public string? FirstTimestamp { get; set; }
    public string? LastTimestamp { get; set; }
    public List<CsvIssue> ValidationIssues { get; set; } = new();
}
