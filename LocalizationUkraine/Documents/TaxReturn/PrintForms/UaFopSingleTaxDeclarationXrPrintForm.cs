#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Бланк F0103309 — декларація єдиного податку 3 групи ФОП. Не XML кабінету.
// Рядок 07 = 15 %. Рядок 23 = 1 % з доходу. Додатки порожні.
public partial class UaFopSingleTaxDeclarationXrPrintForm : PrintFormBase
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
        var rows = await context.GetService<IUaTaxFiling>().ListFopSingleTaxDeclarationAsync(doc);
        var n = 0;
        var total = 0m;
        var mpz = rows.FirstOrDefault(r => r.Row == "14.2").Amount;
        var disclaimer = mpz == 0m
            ? "F0103309; додатки F0133109 і F0133209 порожні"
            : "F0103309; додаток F0133209 МПЗ заповнено";
        foreach (var row in rows)
        {
            n++;
            if (row.Row == "14") total = row.Amount;
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                seller,
                period,
                disclaimer,
                n,
                row.Caption,
                "",
                row.Row,
                0m,
                row.Amount,
                total));
        }
        if (n == 0)
            table.Add(Row(doc.ID ?? "", DateText(doc.DocumentDate), seller, period, "F0103309", 0, "", "", "", 0m, 0m, 0m));
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
