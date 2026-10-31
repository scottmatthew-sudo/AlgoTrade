using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlgoTrade.Data.Turso;


public sealed class TursoClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _url;

    public TursoClient(string url, string token)
    {
        _url = url.TrimEnd('/');
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add("X-Client", "algotrade-csharp-1.0");
    }


    public static JsonNode EncodeArg(object? v) => v switch
    {
        null => new JsonObject { ["type"] = "null" },
        long l => new JsonObject { ["type"] = "integer", ["value"] = l.ToString(CultureInfo.InvariantCulture) },
        int i => new JsonObject { ["type"] = "integer", ["value"] = i.ToString(CultureInfo.InvariantCulture) },
        double d => new JsonObject { ["type"] = "float", ["value"] = d },
        float f => new JsonObject { ["type"] = "float", ["value"] = f },
        string s => new JsonObject { ["type"] = "text", ["value"] = s },
        bool b => new JsonObject { ["type"] = "integer", ["value"] = b ? "1" : "0" },
        _ => throw new NotSupportedException($"Unsupported arg type {v.GetType().Name}")
    };

    /// <summary>Executes a batch of statements in one pipeline request. Closes the session afterwards.</summary>
    public async Task<TursoResult[]> ExecuteAsync(IReadOnlyList<TursoStmt> stmts, CancellationToken ct = default)
    {
        var arr = new JsonArray();
        foreach (var s in stmts)
        {
            var st = new JsonObject { ["sql"] = s.Sql };
            if (s.Args is { Length: > 0 })
            {
                var args = new JsonArray();
                foreach (var a in s.Args) args.Add(EncodeArg(a));
                st["args"] = args;
            }
            arr.Add(new JsonObject { ["type"] = "execute", ["stmt"] = st });
        }
        arr.Add(new JsonObject { ["type"] = "close" });

        var payload = new JsonObject { ["requests"] = arr }.ToJsonString();

        using var req = new HttpRequestMessage(HttpMethod.Post, _url + "/v2/pipeline")
        { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new TursoException($"Turso HTTP {(int)resp.StatusCode}: {Trunc(body, 300)}");

        return ParsePipeline(body);
    }

    public Task<TursoResult[]> ExecuteAsync(params TursoStmt[] stmts)
        => ExecuteAsync((IReadOnlyList<TursoStmt>)stmts);

    /// <summary>Convenience: run one statement, throw on error, return the single result.</summary>
    public async Task<TursoResult> OneAsync(string sql, params object?[] args)
    {
        var results = await ExecuteAsync(new[] { new TursoStmt(sql, args) }).ConfigureAwait(false);
        var r = results[0];
        r.EnsureOk();
        return r;
    }


    internal static TursoResult[] ParsePipeline(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var outList = new List<TursoResult>();
        if (!doc.RootElement.TryGetProperty("results", out var results))
            throw new TursoException($"Turso: unexpected pipeline response: {Trunc(body, 300)}");

        foreach (var r in results.EnumerateArray())
        {
            string type = r.TryGetProperty("type", out var tp) ? tp.GetString() ?? "" : "";
            if (type == "error")
            {
                string msg = "unknown error";
                if (r.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var m))
                    msg = m.GetString() ?? msg;
                outList.Add(TursoResult.Err(msg));
                continue;
            }
            if (!r.TryGetProperty("response", out var response))
            { outList.Add(TursoResult.Err("missing response")); continue; }

            if (!response.TryGetProperty("result", out var result))
            { outList.Add(new TursoResult()); continue; }   // e.g. close → skip row

            var res = new TursoResult();
            if (result.ValueKind == JsonValueKind.Object)
            {
                if (result.TryGetProperty("affected_row_count", out var aff) && aff.ValueKind == JsonValueKind.Number)
                    res.AffectedRowCount = aff.GetInt64();
                res.LastInsertRowId = ReadRowId(result);
                if (result.TryGetProperty("cols", out var cols))
                    res.Cols = cols.EnumerateArray()
                        .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                        .ToArray();
                if (result.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in rows.EnumerateArray())
                        res.Rows.Add(row.EnumerateArray().Select(ParseCell).ToArray());
                }
            }
            outList.Add(res);
        }
        return outList.ToArray();
    }

    private static long? ReadRowId(JsonElement result)
    {
        if (!result.TryGetProperty("last_insert_rowid", out var lid)) return null;
        if (lid.ValueKind == JsonValueKind.Number) return lid.GetInt64();
        if (lid.ValueKind == JsonValueKind.String && long.TryParse(lid.GetString(), out var v)) return v;
        if (lid.ValueKind == JsonValueKind.Object && lid.TryGetProperty("value", out var iv))
            return long.TryParse(iv.ValueKind == JsonValueKind.String ? iv.GetString() : iv.GetRawText(), out var v2) ? v2 : null;
        return null;
    }

    internal static object? ParseCell(JsonElement c)
    {
        if (c.ValueKind == JsonValueKind.Null) return null;
        if (c.ValueKind == JsonValueKind.Object)
        {
            string type = c.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            if (!c.TryGetProperty("value", out var v)) return null;
            switch (type)
            {
                case "integer":
                {
                    string raw = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "0" : v.GetRawText();
                    return long.Parse(raw, CultureInfo.InvariantCulture);
                }
                case "float":
                    return v.ValueKind == JsonValueKind.Number ? v.GetDouble()
                         : double.Parse(v.GetString() ?? "0", CultureInfo.InvariantCulture);
                case "text":
                case "blob":
                    return v.GetString();
                default:
                    return v.ToString();
            }
        }
        return c.ValueKind switch
        {
            JsonValueKind.Number => c.GetDouble(),
            JsonValueKind.String => c.GetString(),
            JsonValueKind.True => 1L,
            JsonValueKind.False => 0L,
            _ => null,
        };
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public void Dispose() => _http.Dispose();
}
