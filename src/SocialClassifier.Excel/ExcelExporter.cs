using ClosedXML.Excel;
using SocialClassifier.Core;

namespace SocialClassifier.Excel;

public sealed class ExcelExporter : IExporter
{
    public void Export(string path, IReadOnlyList<AnalysisRow> rows, IReadOnlyList<Category> categories)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Analysis");

        // Without classification (Laya disabled) the probability, label and category columns are left out.
        var classified = rows.Any(r => r.Probabilities.Count > 0);

        var headers = new List<string> { "Main Post", "My Reply", "Post Type" };
        if (classified)
        {
            headers.AddRange(["Decision Probability", "Top Label"]);
            headers.AddRange(categories.Select(c => c.DisplayName));
        }
        headers.AddRange(["Date (UTC)", "Tweet ID", "URL"]);

        for (var i = 0; i < headers.Count; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        var r = 2;
        foreach (var row in rows)
        {
            var item = row.Item;
            var isReply = item.Type == PostType.MyReply;
            var col = 1;

            ws.Cell(r, col++).Value = isReply ? item.ParentText ?? "" : item.Text;
            ws.Cell(r, col++).Value = isReply ? item.Text : "";
            ws.Cell(r, col++).Value = isReply ? "My Reply" : "My Post";
            if (classified)
            {
                ws.Cell(r, col++).Value = row.DecisionProbability;
                ws.Cell(r, col++).Value = row.TopLabel;
                foreach (var c in categories)
                    ws.Cell(r, col++).Value = row.Probabilities[c.Key];
            }
            ws.Cell(r, col++).Value = item.CreatedAt.UtcDateTime;
            ws.Cell(r, col++).Value = item.Id;
            ws.Cell(r, col).Value = $"https://x.com/i/status/{item.Id}";
            r++;
        }

        var last = Math.Max(r - 1, 2);
        var header = ws.Range(1, 1, 1, headers.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        ws.SheetView.FreezeRows(1);

        // Probability columns as percentages (Decision Probability = col 4, categories start at col 6).
        if (classified)
        {
            ws.Range(2, 4, last, 4).Style.NumberFormat.Format = "0.0%";
            ws.Range(2, 6, last, 5 + categories.Count).Style.NumberFormat.Format = "0.0%";
        }

        ws.Range(2, 1, last, 2).Style.Alignment.WrapText = true;
        ws.Range(2, 1, last, headers.Count).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        for (var i = 3; i <= headers.Count; i++) ws.Column(i).AdjustToContents();
        ws.Column(1).Width = 60;
        ws.Column(2).Width = 60;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        wb.SaveAs(path);
    }
}
