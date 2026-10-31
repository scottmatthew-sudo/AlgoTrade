using System.Globalization;
using AlgoTrade.Core.Domain;

namespace AlgoTrade.Core.Import;

public sealed record CsvParseResult(List<Candle> Candles, List<CsvIssue> Issues, int Read, int Dups, int OutOfOrder);

public static class CsvCandleParser
{
    private static readonly string[] Required = { "timestamp", "open", "high", "low", "close", "volume" };

    public static CsvParseResult Parse(string text)
    {
        var issues = new List<CsvIssue>();
        text = text.TrimStart('\ufeff');                       // BOM insurance for direct API posts

        var allLines = text.Split("\r\n").SelectMany(l => l.Split('\n')).ToList();

        /* Binance dumps arrive headerless with a unix open_time and 12
           columns. Detect that layout and rewrite every line to the
           canonical 6-col shape BEFORE validation; rowOf[] carries each
           parsed line's ORIGINAL file row number so issues stay true. */
        List<string> lines;
        List<int> rowOf;
        int layout = DetectBinanceLayout(allLines);            // 0 std · 1 headerless · 2 headered
        if (layout == 0)
        {
            lines = allLines.Where(l => l.Trim().Length > 0).ToList();
            rowOf = Enumerable.Range(1, lines.Count).ToList();
        }
        else
        {
            (lines, rowOf) = NormalizeBinance(allLines, layout == 2);
        }

        if (lines.Count == 0)
        {
            issues.Add(new CsvIssue(0, "file", "File is empty", true));
            return new CsvParseResult(new List<Candle>(), issues, 0, 0, 0);
        }

        var header = lines[0].Split(',').Select(s => HeaderAlias(s.Trim().ToLowerInvariant())).ToList();
        var idx = new Dictionary<string, int>();
        for (int k = 0; k < Required.Length; k++) idx[Required[k]] = header.IndexOf(Required[k]);

        if (idx["timestamp"] < 0 || idx["close"] < 0)
        {
            issues.Add(new CsvIssue(1, "header",
                "Missing required columns (need at least timestamp + close; full OHLCV recommended)", true));
            return new CsvParseResult(new List<Candle>(), issues, lines.Count - 1, 0, 0);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candles = new List<Candle>();
        int outOfOrder = 0, dups = 0;
        string prevTs = "";

        for (int r = 1; r < lines.Count; r++)
        {
            var cells = lines[r].Split(',').Select(s => s.Trim()).ToArray();
            int rowNo = rowOf[r];                              // original file row number

            string ts = idx["timestamp"] >= 0 ? cells[idx["timestamp"]] : "";
            double o = Num(Cell(cells, idx["open"]));
            double h = Num(Cell(cells, idx["high"]));
            double l = Num(Cell(cells, idx["low"]));
            double c = Num(Cell(cells, idx["close"]));
            double v = Num(Cell(cells, idx["volume"]));
            if (double.IsNaN(v)) v = 0;

            var t = ParseTs(ts);
            if (string.IsNullOrEmpty(ts) || t is null)
            {
                issues.Add(new CsvIssue(rowNo, "timestamp", $"Unparseable timestamp \"{ts}\"", true));
                continue;
            }
            if (double.IsNaN(o) || double.IsNaN(c) || double.IsNaN(h) || double.IsNaN(l))
            {
                issues.Add(new CsvIssue(rowNo, "prices", "Non-numeric price value", true));
                continue;
            }
            if (o <= 0 || h <= 0 || l <= 0 || c <= 0)
            {
                issues.Add(new CsvIssue(rowNo, "prices", "Non-positive price value", true));
                continue;
            }

            string iso = t.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(iso, prevTs) < 0) outOfOrder++;
            if (seen.Contains(iso))
            {
                dups++;
                issues.Add(new CsvIssue(rowNo, "timestamp", $"Duplicate timestamp {iso} — kept first occurrence (D16)", true));
                continue;
            }
            if (h < System.Math.Max(o, System.Math.Max(c, l)) || l > System.Math.Min(o, c) || (o > 0 && h <= 0))
            {
                issues.Add(new CsvIssue(rowNo, "ohlc", "Inconsistent candle (high≥open/close/low, low≤open/close required)", true));
                continue;
            }

            seen.Add(iso);           // mirrors JS: only accepted rows claim their timestamp
            prevTs = iso;
            candles.Add(new Candle(iso, o, h, l, c, v));
        }

        if (outOfOrder > 0)
            issues.Add(new CsvIssue(0, "order",
                $"{outOfOrder} row(s) out of chronological order — accepted and sorted ascending (D17)", false));

        candles.Sort((a, b) => string.CompareOrdinal(a.Ts, b.Ts));
        return new CsvParseResult(candles, issues, lines.Count - 1, dups, outOfOrder);
    }

    private static string Cell(string[] cells, int i) => i >= 0 && i < cells.Length ? cells[i] : "";

    /// <summary>Number('') === 0; Number('garbage') === NaN — both mirrored.</summary>
    private static double Num(string s)
    {
        if (s.Length == 0) return 0;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }

    /// <summary>Date.parse(): ISO "…T…" taken as-is, "…" gets 'T' + 'Z'. Unix-epoch
    /// digit cells (10–17 chars ⇒ seconds / ms / µs) are also accepted → UTC. Or null.</summary>
    private static DateTime? ParseTs(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (s.Length is >= 10 and <= 17 && s.All(c => c is >= '0' and <= '9'))
        {
            long n = long.Parse(s, CultureInfo.InvariantCulture);
            long ms = s.Length switch { <= 10 => n * 1000, <= 15 => n, _ => n / 1000 };
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }
        try
        {
            string str = s.Contains('T') ? s : s.Replace(" ", "T") + "Z";
            var dt = DateTime.Parse(str, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        catch (FormatException) { return null; }
    }
    /// <summary>Common dataset-export header synonyms → canonical names. Lets
    /// Kaggle/CryptoDataDownload files (unix,date,…,close) import as-is.</summary>
    private static string HeaderAlias(string h) => h switch
    {
        "unix" or "date" or "time" or "datetime" or "open_time" or "opentime" or "open time" => "timestamp",
        _ => h,
    };

    /* data.binance.vision kline files are headerless, 12 columns, unix-ms open_time:
         open_time,open,high,low,close,volume,close_time,quote_volume,trades,taker_base,taker_quote,ignore
       (legacy 2017–2021 files use "2017-08-17 04:00:00" instead of epoch — both covered).
       Returns 0 = standard CSV · 1 = headerless dump · 2 = "open_time,…" header row. */
    private static int DetectBinanceLayout(List<string> allLines)
    {
        var first = allLines.FirstOrDefault(l => l.Trim().Length > 0);
        if (first is null) return 0;

        var head = first.Split(',').Select(s => s.Trim()).ToArray();
        if (head.Length < 11) return 0;                        // OHLCV files have ~6–8 columns

        if (head[0].Equals("open_time", StringComparison.OrdinalIgnoreCase)) return 2;

        bool epochLike = head[0].Length is >= 10 and <= 17 && head[0].All(c => c is >= '0' and <= '9');
        bool dateLike  = DateTime.TryParse(head[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        bool priceLike = double.TryParse(Cell(head, 1), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        return (epochLike || dateLike) && priceLike ? 1 : 0;
    }

    /// <summary>Rewrite a raw Binance kline dump to canonical 6-col rows, converting
    /// the open_time cell (unix s/ms/µs or legacy date string) to ISO. Row numbers
    /// point back into the original file so validation issues stay accurate.</summary>
    private static (List<string> Lines, List<int> RowOf) NormalizeBinance(List<string> allLines, bool hasHeader)
    {
        var lines = new List<string> { "timestamp,open,high,low,close,volume" };
        var rowOf = new List<int> { 0 };                       // synthetic header — never reported

        int start = hasHeader ? allLines.FindIndex(l => l.Trim().Length > 0) + 1 : 0;
        for (int i = start; i < allLines.Count; i++)
        {
            if (allLines[i].Trim().Length == 0) continue;
            var c = allLines[i].Split(',').Select(s => s.Trim()).ToArray();
            if (c.Length < 2) continue;

            lines.Add(string.Join(',', ToIsoFromEpoch(c[0]) ?? c[0],
                Cell(c, 1), Cell(c, 2), Cell(c, 3), Cell(c, 4), Cell(c, 5)));
            rowOf.Add(i + 1);
        }
        return (lines, rowOf);
    }

    /// <summary>unix epoch cell → ISO "yyyy-MM-ddTHH:mm:ss" (UTC). Digit length picks
    /// seconds / ms (Binance klines) / µs. Null for anything else.</summary>
    private static string? ToIsoFromEpoch(string s)
    {
        if (s.Length is not (>= 10 and <= 17) || !s.All(c => c is >= '0' and <= '9')) return null;
        long n = long.Parse(s, CultureInfo.InvariantCulture);
        long ms = s.Length switch { <= 10 => n * 1000, <= 15 => n, _ => n / 1000 };
        return DateTimeOffset.FromUnixTimeMilliseconds(ms)
                             .UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }
}
