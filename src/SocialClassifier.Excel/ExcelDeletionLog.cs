using ClosedXML.Excel;
using SocialClassifier.Core;

namespace SocialClassifier.Excel;

/// <summary>
/// Records deletions on a "Deleted" sheet inside an existing workbook (e.g. output/analysis.xlsx),
/// leaving the other sheets untouched. Creates the workbook if it does not exist.
/// </summary>
public sealed class ExcelDeletionLog(string path) : IDeletionLog
{
    private const string SheetName = "Deleted";
    private readonly List<DeletionEntry> _unsaved = [];

    public IReadOnlySet<string> LoadDeletedIds()
    {
        var ids = new HashSet<string>();
        if (!File.Exists(path)) return ids;

        using var wb = OpenWorkbook();
        if (!wb.TryGetWorksheet(SheetName, out var ws)) return ids;

        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var id = ws.Cell(r, 1).GetString();
            if (id.Length > 0) ids.Add(id);
        }
        return ids;
    }

    public void Add(DeletionEntry entry) => _unsaved.Add(entry);

    public void Save()
    {
        if (_unsaved.Count == 0) return;

        using var wb = File.Exists(path) ? OpenWorkbook() : new XLWorkbook();
        if (!wb.TryGetWorksheet(SheetName, out var ws))
        {
            ws = wb.AddWorksheet(SheetName);
            string[] headers = ["Tweet ID", "My Reply", "Deleted At (UTC)", "Result"];
            for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
            var header = ws.Range(1, 1, 1, headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.LightGray;
            ws.SheetView.FreezeRows(1);
            ws.Column(2).Width = 80;
            ws.Column(3).Width = 20;
        }

        var row = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
        foreach (var e in _unsaved)
        {
            ws.Cell(row, 1).SetValue(e.Id);   // text, so long IDs are not shown in scientific notation
            ws.Cell(row, 2).Value = e.Text;
            ws.Cell(row, 3).Value = e.DeletedAt.UtcDateTime;
            ws.Cell(row, 4).Value = e.Outcome == DeleteOutcome.Deleted ? "Deleted" : "Already gone";
            row++;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        wb.SaveAs(path);
        _unsaved.Clear();
    }

    // Read through memory so the file handle is released before SaveAs writes to the same path.
    private XLWorkbook OpenWorkbook() => new(new MemoryStream(File.ReadAllBytes(path)));
}
