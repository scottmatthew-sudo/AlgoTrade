using Xunit;

using AlgoTrade.Core.Import;

namespace AlgoTrade.Tests;

/* ═══════════════════════════════════════════════════════════════
   CSV parser rule tests — pin the ImportPipeline validation
   contract (D16 duplicate handling, D17 out-of-order tolerance).
   ═══════════════════════════════════════════════════════════════ */
public class CsvParserTests
{
    private const string Header = "timestamp,open,high,low,close,volume";

    [Fact]
    public void Parses_Valid_Rows_And_Sorts_Ascending()
    {
        var csv = string.Join("\n",
            Header,
            "2025-01-01 00:02:00,30,31,29,30.5,100",
            "2025-01-01 00:00:00,10,11,9,10.5,80",
            "2025-01-01 00:01:00,20,21,19,20.5,90");

        var r = CsvCandleParser.Parse(csv);
        Assert.Equal(3, r.Candles.Count);
        Assert.Equal(3, r.Read);
        Assert.Equal("2025-01-01T00:00:00", r.Candles[0].Ts);   // sorted despite input order
        Assert.Equal("2025-01-01T00:02:00", r.Candles[2].Ts);
        Assert.Equal(1, r.OutOfOrder);                          // first row was newer than next
        Assert.Contains(r.Issues, i => i.Col == "order" && !i.Rejected);
    }

    [Fact]
    public void Duplicate_Timestamp_Rejected_Keeps_First_D16()
    {
        var csv = string.Join("\n",
            Header,
            "2025-01-01T00:00:00,10,11,9,10.5,80",
            "2025-01-01T00:00:00,20,21,19,20.5,90");   // same ts — must be rejected

        var r = CsvCandleParser.Parse(csv);
        Assert.Single(r.Candles);
        Assert.Equal(10.5, r.Candles[0].C, 9);         // FIRST occurrence kept
        Assert.Equal(1, r.Dups);
        Assert.Contains(r.Issues, i => i.Rejected && i.Msg.Contains("D16"));
    }

    [Fact]
    public void Inconsistent_Ohlc_Rejected()
    {
        var csv = string.Join("\n",
            Header,
            "2025-01-01T00:00:00,10,8,9,10.5,80");     // high < open

        var r = CsvCandleParser.Parse(csv);
        Assert.Empty(r.Candles);
        Assert.Contains(r.Issues, i => i.Rejected && i.Col == "ohlc");
    }

    [Fact]
    public void Bad_Timestamp_Rejected()
    {
        var csv = string.Join("\n",
            Header,
            "yesterday-ish,10,11,9,10.5,80");

        var r = CsvCandleParser.Parse(csv);
        Assert.Empty(r.Candles);
        Assert.Contains(r.Issues, i => i.Rejected && i.Col == "timestamp");
    }

    [Fact]
    public void Non_Numeric_Price_Rejected()
    {
        var csv = string.Join("\n",
            Header,
            "2025-01-01T00:00:00,bad,11,9,10.5,80");

        var r = CsvCandleParser.Parse(csv);
        Assert.Empty(r.Candles);
        Assert.Contains(r.Issues, i => i.Rejected && i.Col == "prices");
    }

    [Fact]
    public void Missing_Header_Columns_Fail_Fast()
    {
        var csv = "open,high,low,close\n10,11,9,10.5";
        var r = CsvCandleParser.Parse(csv);
        Assert.Empty(r.Candles);
        Assert.Contains(r.Issues, i => i.Rejected && i.Col == "header");
    }

    [Fact]
    public void Empty_File_Reported()
    {
        var r = CsvCandleParser.Parse("  \n \r\n");
        Assert.Empty(r.Candles);
        Assert.Equal(0, r.Read);
        Assert.Contains(r.Issues, i => i.Col == "file");
    }

    [Fact]
    public void Missing_Volume_Cell_Becomes_Zero()
    {
        var csv = "timestamp,open,high,low,close\n2025-01-01T00:00:00,10,11,9,10.5";
        var r = CsvCandleParser.Parse(csv);
        Assert.Single(r.Candles);
        Assert.Equal(0, r.Candles[0].V, 9);
    }

    /* ── Binance raw kline dumps + epoch timestamps (server-side additions) ── */

    [Fact]
    public void Binance_Headerless_12Col_Dump_Is_Normalized()
    {
        // data.binance.vision monthly kline layout: no header, 12 cols, unix-ms open_time
        var csv = string.Join("\n",
            "1704067200000,42000.00,42500.10,41980.50,42350.75,123.45678,1704070799999,5234567.89,2345,61.10,2591100.11,0",
            "1704070800000,42350.75,43100.00,42300.25,43010.10,150.00000,1704074399999,6423456.78,3001,70.22,3022345.67,0");

        var r = CsvCandleParser.Parse(csv);
        Assert.Equal(2, r.Candles.Count);
        Assert.Equal(2, r.Read);
        Assert.Equal("2024-01-01T00:00:00", r.Candles[0].Ts);
        Assert.Equal("2024-01-01T01:00:00", r.Candles[1].Ts);
        Assert.Equal(42350.75, r.Candles[0].C, 9);
        Assert.Equal(123.45678, r.Candles[0].V, 9);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Binance_Headered_Dump_Reports_Original_Row_Numbers()
    {
        var csv = string.Join("\n",
            "open_time,open,high,low,close,volume,close_time,quote_volume,count,taker_base,taker_quote,ignore",
            "2024-01-01T00:00:00,42000,42500,41980,42350.75,123.45,0,0,0,0,0,0",
            "not-a-date,42000,42500,41980,42350.75,123.45,0,0,0,0,0,0");

        var r = CsvCandleParser.Parse(csv);
        Assert.Single(r.Candles);
        var issue = r.Issues.Single(i => i.Rejected);
        Assert.Equal(3, issue.Row);                            // original file row, not normalized
    }

    [Fact]
    public void Unix_Epoch_Timestamps_Accepted_In_Canonical_Csv()
    {
        var csv = string.Join("\n",
            Header,
            "1704067200000,10,11,9,10.5,80",                   // ms
            "1704070800,20,21,19,20.5,90");                    // seconds also fine

        var r = CsvCandleParser.Parse(csv);
        Assert.Equal(2, r.Candles.Count);
        Assert.Equal("2024-01-01T00:00:00", r.Candles[0].Ts);
        Assert.Equal("2024-01-01T01:00:00", r.Candles[1].Ts);
    }

    [Fact]
    public void Common_Header_Aliases_Map_To_Canonical_Names()
    {
        // Kaggle / CryptoDataDownload style export
        var csv = string.Join("\n",
            "unix,date,symbol,open,high,low,close,Volume BTC",
            "1704067200,2024-01-01 00:00:00,BTCUSDT,42000,42500,41980,42350.75,12.5");

        var r = CsvCandleParser.Parse(csv);
        Assert.Single(r.Candles);
        Assert.Equal("2024-01-01T00:00:00", r.Candles[0].Ts);
        Assert.Equal(42350.75, r.Candles[0].C, 9);
        Assert.Equal(0, r.Candles[0].V, 9);                    // no plain volume column → 0
    }
}
