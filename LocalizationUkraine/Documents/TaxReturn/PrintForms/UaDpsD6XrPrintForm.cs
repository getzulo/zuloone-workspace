#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Бланк J0510611 (додаток Д6). Код спецстажу з Employee.DpsTenureGround.
public partial class UaDpsD6XrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row("", "", "", "", "", 0, "", "", "", 0m, 0m, 0m));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var doc = await documents.GetDocumentAsync<TaxReturn>(context.RecordId);
        if (doc == null)
        {
            table.Add(Row("", "", "", "", "", 0, "", "", "", 0m, 0m, 0m));
            return table;
        }

        var display = context.GetService<IReferenceDisplay>();
        var seller = doc.LegalEntity == Guid.Empty
            ? ""
            : await display.FormatAsync("LegalEntity", doc.LegalEntity, context.CancellationToken) ?? "";
        var period = $"{DateText(doc.PeriodFrom)} — {DateText(doc.PeriodTo)}";
        var rows = await context.GetService<IUaTaxFiling>()
            .ListDpsD6Async(doc.LegalEntity, doc.PeriodFrom, doc.PeriodTo);
        var n = 0;
        var total = 0m;
        foreach (var row in rows)
        {
            n++;
            total += row.Days;
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                seller,
                period,
                "J0510611",
                n,
                row.Name,
                row.TaxCard,
                row.Ground,
                row.Days,
                0m,
                total));
        }
        if (n == 0)
            table.Add(Row(doc.ID ?? "", DateText(doc.DocumentDate), seller, period, "J0510611", 0, "", "", "", 0m, 0m, 0m));
        return table;
    }

    private static object Row(
        string number, string documentDate, string seller, string notes, string disclaimer,
        int lineNo, string employee, string employeeId, string taxCode,
        decimal taxBase, decimal amount, decimal total)
        => new
        {
            Number = number, DocumentDate = documentDate, Seller = seller, Notes = notes,
            Disclaimer = disclaimer, LineNo = lineNo, Employee = employee, EmployeeId = employeeId,
            TaxCode = taxCode, TaxBase = taxBase, Amount = amount, Total = total,
        };

    private static string DateText(DateTime value)
        => value.Year >= 1902 ? value.ToString("yyyy-MM-dd") : "";
}
