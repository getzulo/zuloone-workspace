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
        var rows = await context.GetService<IUaTaxFiling>().ListSingleTaxDeclarationAsync(doc);
        var n = 0;
        var total = 0m;
        var row2 = rows.FirstOrDefault(r => r.Row == "2");
        var disclaimer = row2.Col3 == 0m && row2.Col4 == 0m
            ? "J0103509; рядок 2 порожній коли немає UA-EP6/UA-EP10; не UA-EP15"
            : "J0103509; рядок 2 з UA-EP6/UA-EP10; не UA-EP15";
        foreach (var row in rows)
        {
            n++;
            if (row.Row == "10") total = row.Col3 + row.Col4;
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
