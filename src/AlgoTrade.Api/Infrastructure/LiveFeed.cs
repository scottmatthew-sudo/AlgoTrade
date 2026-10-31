using System.Globalization;
using System.Text;
using System.Text.Json;
using AlgoTrade.Core.Domain;
using AlgoTrade.Core.Import;

namespace AlgoTrade.Api.Infrastructure;

public sealed record TickerRow(string Symbol, double Price, double Prev, double Delta);

public sealed class LiveFeedState
{
    private readonly object _lock = new();
    private readonly List<Candle> _candles = new();
    private readonly int _ringCap = 720;

    public string Symbol { get; private set; } = "BTCUSDT";
    public string Interval { get; private set; } = "1m";
    public string Status { get; private set; } = "offline";   // live | cached | offline
    public string? LastError { get; private set; }
    public DateTime? LastUpdateUtc { get; private set; }
    private List<TickerRow> _ticker = new();

    public void Seed(string symbol, string interval, List<Candle> candles)
    {
        lock (_lock)
        {
            Symbol = symbol; Interval = interval;
            _candles.Clear(); _candles.AddRange(candles.TakeLast(_ringCap));
            Status = "cached";
        }
    }

    public void AppendCandle(string symbol, string interval, Candle c)
    {
        lock (_lock)
        {
            Symbol = symbol; Interval = interval;
            if (_candles.Count > 0 && string.CompareOrdinal(_candles[^1].Ts, c.Ts) >= 0) return;
            _candles.Add(c);
            if (_candles.Count > _ringCap) _candles.RemoveAt(0);
        }
    }

    public void SetTicker(List<TickerRow> ticker)
    {
        lock (_lock) _ticker = ticker;
    }

    public void SetLive()
    {
        lock (_lock) { Status = "live"; LastError = null; LastUpdateUtc = DateTime.UtcNow; }
    }

    public void MarkError(string message)
    {
        lock (_lock)
        {
            LastError = message;
            if (_candles.Count > 0) Status = "cached"; else Status = "offline";
        }
    }

    public List<Candle> SnapshotCandles(int limit)
    {
        lock (_lock) return _candles.TakeLast(limit).ToList();
    }

    public List<TickerRow> SnapshotTicker()
    {
        lock (_lock) return _ticker.ToList();
    }

    public (string Status, string? LastError, DateTime? LastUpdate) SnapshotMeta()
    {
        lock (_lock) return (Status, LastError, LastUpdateUtc);
    }
}

/* CSV fallback store: one append-only file per symbol/interval */
public sealed class CsvStore
{
    private readonly string _dir;
    private readonly object _io = new();

    public CsvStore(IWebHostEnvironment env)
        => _dir = Path.Combine(env.ContentRootPath, "data", "live");

    private string PathFor(string symbol, string interval)
        => Path.Combine(_dir, $"{symbol}_{interval}.csv");

    public void Append(string symbol, string interval, Candle c)
    {
        lock (_io)
        {
            Directory.CreateDirectory(_dir);
            var p = PathFor(symbol, interval);
            if (!File.Exists(p))
                File.AppendAllText(p, "timestamp,open,high,low,close,volume\n", Encoding.UTF8);
            File.AppendAllText(p,
                $"{c.Ts},{Inv(c.O)},{Inv(c.H)},{Inv(c.L)},{Inv(c.C)},{Inv(c.V)}\n", Encoding.UTF8);
        }
    }

    /// <summary>Reads the last n candles from the CSV (through the same validator as imports).</summary>
    public List<Candle> ReadTail(string symbol, string interval, int n)
    {
        lock (_io)
        {
            var p = PathFor(symbol, interval);
            if (!File.Exists(p)) return new List<Candle>();
            try
            {
                var parsed = CsvCandleParser.Parse(File.ReadAllText(p));
                return parsed.Candles.TakeLast(n).ToList();
            }
            catch { return new List<Candle>(); }
        }
    }

    private static string Inv(double d) => d.ToString(CultureInfo.InvariantCulture);
}
public sealed class BinancePoller : BackgroundService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly string[] TickerSymbols = { "BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT", "XRPUSDT" };

    private readonly LiveFeedState _state;
    private readonly CsvStore _csv;
    private readonly ILogger<BinancePoller> _log;
    private readonly string _base;
    private readonly string _symbol;
    private readonly string _interval;
    private readonly int _pollSeconds;
    private long _lastAppendedMs;

    public BinancePoller(LiveFeedState state, CsvStore csv, IConfiguration cfg, ILogger<BinancePoller> log)
    {
        _state = state; _csv = csv; _log = log;
        _base = cfg["Binance:BaseUrl"] ?? "https://api.binance.com";
        _symbol = cfg["Binance:Symbol"] ?? "BTCUSDT";
        _interval = cfg["Binance:Interval"] ?? "1m";
        _pollSeconds = int.TryParse(cfg["Binance:PollSeconds"], out var s) ? s : 15;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        /* seed from the CSV fallback so the UI is never empty (CACHED) */
        try
        {
            var tail = _csv.ReadTail(_symbol, _interval, 720);
            if (tail.Count > 0)
            {
                _state.Seed(_symbol, _interval, tail);
                _lastAppendedMs = ToMs(Candle.ParseTs(tail[^1].Ts));
                _log.LogInformation("Live feed seeded from CSV: {N} candles", tail.Count);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "CSV seed failed"); }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(System.Math.Max(5, _pollSeconds)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnce(stoppingToken);
                _state.SetLive();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _state.MarkError(ex.Message);
                _log.LogWarning("Live feed degraded: {Message}", ex.Message);
            }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOnce(CancellationToken ct)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using (var resp = await Http.GetAsync(
            $"{_base}/api/v3/klines?symbol={_symbol}&interval={_interval}&limit=2", ct))
        {
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            Candle? closed = null;
            foreach (var k in doc.RootElement.EnumerateArray())
            {
                long closeTime = k[6].GetInt64();
                if (closeTime <= nowMs) closed = KlineToCandle(k);
            }
            if (closed is not null)
            {
                long openMs = ToMs(Candle.ParseTs(closed.Ts));
                if (openMs > _lastAppendedMs)
                {
                    _state.AppendCandle(_symbol, _interval, closed);
                    _csv.Append(_symbol, _interval, closed);
                    _lastAppendedMs = openMs;
                }
            }
        }
        var symbolsJson = JsonSerializer.Serialize(TickerSymbols);
        using (var resp = await Http.GetAsync(
            $"{_base}/api/v3/ticker/24hr?symbols={Uri.EscapeDataString(symbolsJson)}", ct))
        {
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var rows = new List<TickerRow>();
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                rows.Add(new TickerRow(
                    t.GetProperty("symbol").GetString() ?? "",
                    Dbl(t.GetProperty("lastPrice")),
                    Dbl(t.GetProperty("prevClosePrice")),
                    Dbl(t.GetProperty("priceChangePercent"))));
            }
            _state.SetTicker(rows);
        }
    }

    private static long ToMs(DateTime utc)
        => (long)(utc - DateTime.UnixEpoch).TotalMilliseconds;

    private static Candle KlineToCandle(JsonElement k)
    {
        return new Candle(
            DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64()).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            DblStr(k[1]), DblStr(k[2]), DblStr(k[3]), DblStr(k[4]), DblStr(k[5]));
    }

    private static double DblStr(JsonElement e)
        => double.Parse(e.GetString() ?? "0", CultureInfo.InvariantCulture);

    private static double Dbl(JsonElement e)
        => e.ValueKind == JsonValueKind.Number ? e.GetDouble()
         : double.Parse(e.GetString() ?? "0", CultureInfo.InvariantCulture);
}
