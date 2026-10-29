using System.Text.Json.Serialization;

namespace AlgoTrade.Core.Domain;

/* ═══════════════════════════════════════════════════════════════
   Import models (Blueprint §4.5 / D16–D18).

   CsvIssue      — one validation finding; rejected=true means the
                   row was NOT persisted, false = advisory warning.
   ImportReport  — response of POST /datasets/import (FROZEN):
     { ok, datasetId?, issues[], read, accepted, rejected, dups, outOfOrder }
   ═══════════════════════════════════════════════════════════════ */
public sealed class CsvIssue
{
    [JsonPropertyName("row")] public int Row { get; set; }
    [JsonPropertyName("col")] public string Col { get; set; } = "";
    [JsonPropertyName("msg")] public string Msg { get; set; } = "";
    [JsonPropertyName("rejected")] public bool Rejected { get; set; }

    public CsvIssue() { }
    public CsvIssue(int row, string col, string msg, bool rejected)
    { Row = row; Col = col; Msg = msg; Rejected = rejected; }
}

public sealed class ImportReport
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("datasetId")] public long? DatasetId { get; set; }
    [JsonPropertyName("issues")] public List<CsvIssue> Issues { get; set; } = new();
    [JsonPropertyName("read")] public int Read { get; set; }
    [JsonPropertyName("accepted")] public int Accepted { get; set; }
    [JsonPropertyName("rejected")] public int Rejected { get; set; }
    [JsonPropertyName("dups")] public int Dups { get; set; }
    [JsonPropertyName("outOfOrder")] public int OutOfOrder { get; set; }
}
