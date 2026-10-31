using System.Text.Json;
using AlgoTrade.Core.Domain;
using AlgoTrade.Data.Turso;

namespace AlgoTrade.Data.Repos;

public sealed class StrategyRepo
{
    private readonly TursoClient _db;
    public StrategyRepo(TursoClient db) { _db = db; }

    private const string SelectCols =
        "StrategyId, Name, Description, StrategyType, ParametersJson, RiskJson, Version, CreatedAtUtc, UpdatedAtUtc";

    public async Task<List<StrategyConfig>> ListAsync()
    {
        var r = await _db.OneAsync($"SELECT {SelectCols} FROM Strategy ORDER BY StrategyId DESC");
        r.EnsureOk();
        var list = new List<StrategyConfig>(r.RowCount);
        for (int i = 0; i < r.RowCount; i++) list.Add(Map(r, i));
        return list;
    }

    public async Task<StrategyConfig?> GetAsync(long id)
    {
        var r = await _db.OneAsync($"SELECT {SelectCols} FROM Strategy WHERE StrategyId = ?", id);
        r.EnsureOk();
        return r.RowCount == 0 ? null : Map(r, 0);
    }

    public async Task<long> InsertAsync(StrategyConfig s)
    {
        string name = s.Name;

        // UNIQUE name conflict → mirror the forgiving browser store with a suffix.
        // (OneAsync throws TursoException on SQL errors, so the retry is a catch.)
        TursoResult r;
        try
        {
            r = await _db.OneAsync(
                "INSERT INTO Strategy (Name, Description, StrategyType, ParametersJson, RiskJson) VALUES (?,?,?,?,?)",
                name, s.Desc, s.Type, JsonSerializer.Serialize(s.P), JsonSerializer.Serialize(s.Risk));
        }
        catch (TursoException ex) when (ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            r = await _db.OneAsync(
                "INSERT INTO Strategy (Name, Description, StrategyType, ParametersJson, RiskJson) VALUES (?,?,?,?,?)",
                $"{name} · {DateTime.UtcNow:HH:mm:ss}", s.Desc, s.Type,
                JsonSerializer.Serialize(s.P), JsonSerializer.Serialize(s.Risk));
        }
        r.EnsureOk();
        return r.LastInsertRowId ?? 0;
    }

    /// <summary>Bumps version + updatedAt; returns the updated config, or null when the id is unknown.</summary>
    public async Task<StrategyConfig?> UpdateAsync(long id, StrategyConfig s)
    {
        var existing = await GetAsync(id);
        if (existing is null) return null;

        var r = await _db.OneAsync(
            """
            UPDATE Strategy SET Name=?, Description=?, StrategyType=?, ParametersJson=?, RiskJson=?,
              Version=Version+1, UpdatedAtUtc=strftime('%Y-%m-%dT%H:%M:%S','now')
            WHERE StrategyId=?
            """,
            s.Name, s.Desc, s.Type, JsonSerializer.Serialize(s.P), JsonSerializer.Serialize(s.Risk), id);
        r.EnsureOk();
        return await GetAsync(id);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var r = await _db.OneAsync("DELETE FROM Strategy WHERE StrategyId = ?", id);
        r.EnsureOk();
        return r.AffectedRowCount > 0;
    }

    private static StrategyConfig Map(TursoResult r, int row) => new()
    {
        Id = r.L(row, 0),
        Name = r.S(row, 1),
        Desc = r.SN(row, 2),
        Type = r.S(row, 3),
        P = JsonSerializer.Deserialize<Dictionary<string, object?>>(r.S(row, 4)) ?? new(),
        Risk = JsonSerializer.Deserialize<RiskSettings>(r.S(row, 5)) ?? new RiskSettings(),
        Version = (int)r.L(row, 6),
        CreatedAt = r.S(row, 7),
        UpdatedAt = r.S(row, 8),
    };
}
