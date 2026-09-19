#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Quantity-only transfer slip: FromCell → ToCell on the header.
public partial class StockTransferXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row("", "", "", "", 0, "", 0m, ""));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var doc = await documents.GetDocumentAsync<StockTransfer>(context.RecordId);
        if (doc == null)
        {
            table.Add(Row("", "", "", "", 0, "", 0m, ""));
            return table;
        }

        var display = context.GetService<IReferenceDisplay>();
        var ct = context.CancellationToken;
        var fromCell = await NameAsync(display, "StoreCell", doc.FromCell, ct);
        var toCell = await NameAsync(display, "StoreCell", doc.ToCell, ct);

        var n = 0;
        foreach (var line in doc.Lines)
        {
            n++;
            var itemText = await NameAsync(display, "Item", line.Item, ct);
            var unitText = await NameAsync(display, "UnitOfMeasure", line.Unit, ct);
            table.Add(Row(
                doc.ID ?? "", DateText(doc.DocumentDate), fromCell, toCell,
                n, itemText, line.Quantity, unitText));
        }

        if (n == 0)
            table.Add(Row(doc.ID ?? "", DateText(doc.DocumentDate), fromCell, toCell, 0, "", 0m, ""));

        return table;
    }

    private static object Row(
        string number, string documentDate, string location, string notes,
        int lineNo, string item, decimal quantity, string unit)
        => new
        {
            Number = number,
            DocumentDate = documentDate,
            Location = location,
            Notes = notes,
            LineNo = lineNo,
            Item = item,
            Quantity = quantity,
            Unit = unit,
        };

    private static async Task<string> NameAsync(
        IReferenceDisplay display, string dictionary, Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) return "";
        return await display.FormatAsync(dictionary, id, ct) ?? "";
    }

    private static string DateText(DateTime value)
        => value.Year >= 1902 ? value.ToString("yyyy-MM-dd") : "";
}
