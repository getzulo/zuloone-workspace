#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Бланк J0103509 — декларація єдиного податку 3 групи ЮО. Не XML кабінету.
// Графа 3 = 3 %, графа 4 = 5 %. Рядок 2 і додаток МПЗ порожні.
public partial class UaSingleTaxDeclarationXrPrintForm : PrintFormBase
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
        var rows = context.GetService<IUaTaxFiling>().ListSingleTaxDeclaration(doc);
        var n = 0;
        var total = 0m;
        foreach (var row in rows)
        {
            n++;
            if (row.Row == "10") total = row.Col3 + row.Col4;
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                seller,
                period,
                "J0103509; рядок 2 і додаток МПЗ порожні; не UA-EP15",
                n,
                row.Caption,
                "",
                row.Row,
                row.Col3,
                row.Col4,
                total));
        }
        if (n == 0)
            table.Add(Row(doc.ID ?? "", DateText(doc.DocumentDate), seller, period, "J0103509", 0, "", "", "", 0m, 0m, 0m));
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
