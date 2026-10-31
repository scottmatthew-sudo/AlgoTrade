using System.Globalization;

namespace AlgoTrade.Data.Turso;


/* NOTE: sealed class (not a positional record) so the params-style
   constructor is the ONLY constructor — `new TursoStmt(sql, a, b, c)`
   and `new TursoStmt(sql, argsArray)` both bind unambiguously. */
public sealed class TursoStmt
{
    public string Sql { get; }
    public object?[]? Args { get; }
    public TursoStmt(string sql, params object?[]? args) { Sql = sql; Args = args; }
}

public sealed class TursoException : Exception
{
    public TursoException(string message) : base(message) { }
}

public sealed class TursoResult
{
    /* NOTE: set (not init) — TursoClient.ParsePipeline fills these after construction. */
    public bool IsError { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string[] Cols { get; set; } = Array.Empty<string>();
    public List<object?[]> Rows { get; set; } = new();
    public long AffectedRowCount { get; set; }
    public long? LastInsertRowId { get; set; }

    public static TursoResult Err(string msg) => new() { IsError = true, ErrorMessage = msg };
    public long L(int row, int col) => CellAs<long>(row, col);
    public double D(int row, int col) => CellAs<double>(row, col);
    public string S(int row, int col) => CellAs<string>(row, col) ?? "";
    public string? SN(int row, int col) => CellAs<string>(row, col);
    public bool IsNull(int row, int col) => Rows[row][col] is null;
    public int RowCount => Rows.Count;

    private T? CellAs<T>(int row, int col)
    {
        if (row < 0 || row >= Rows.Count) return default;
        var cell = Rows[row][col];
        if (cell is null) return default;
        if (cell is T typed) return typed;
        return (T)Convert.ChangeType(cell, typeof(T), CultureInfo.InvariantCulture);
    }

    /// <summary>Throws when this result carries an error (one-liner guard for repos).</summary>
    public void EnsureOk()
    {
        if (IsError) throw new TursoException(ErrorMessage);
    }
}
