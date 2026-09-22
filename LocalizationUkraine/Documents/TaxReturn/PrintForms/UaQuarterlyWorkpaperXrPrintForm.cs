#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Робоча таблиця ЄСВ+ПДФО+ВЗ на декларації. Не бланк Податкового розрахунку
// ДПС: додатків і XML схеми немає. Друкуємо SocialInsurance і UaPayrollLevy
// за період шапки.
public partial class UaQuarterlyWorkpaperXrPrintForm : PrintFormBase
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
        var filing = context.GetService<IUaTaxFiling>();
        var esv = await filing.ListEsvAsync(doc.LegalEntity, doc.PeriodFrom, doc.PeriodTo);
        var levies = await filing.ListLeviesAsync(doc.LegalEntity, doc.PeriodFrom, doc.PeriodTo);
        var total = 0m;
        var n = 0;
        foreach (var row in esv)
        {
            n++;
            total += row.EmployeeContribution + row.EmployerContribution;
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                seller,
                period,
                "Не бланк Податкового розрахунку ДПС",
                n,
                row.EmployeeName,
                row.EmployeeId,
                "ЄСВ",
                row.EmployeeContribution,
                row.EmployerContribution,
                total));
        }
        foreach (var row in levies)
        {
            n++;
            total += row.Amount;
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                seller,
                period,
                "Не бланк Податкового розрахунку ДПС",
                n,
                row.EmployeeName,
                row.EmployeeId,
                row.TaxCode,
                row.Base,
                row.Amount,
                total));
        }

        if (n == 0)
        {
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                seller,
                period,
                "Не бланк Податкового розрахунку ДПС",
                0,
                "",
                "",
                "",
                0m,
                0m,
                0m));
        }

        return table;
    }

    private static object Row(
        string number,
        string documentDate,
        string seller,
        string notes,
        string disclaimer,
        int lineNo,
        string employee,
        string employeeId,
        string taxCode,
        decimal taxBase,
        decimal amount,
        decimal total)
        => new
        {
            Number = number,
            DocumentDate = documentDate,
            Seller = seller,
            Notes = notes,
            Disclaimer = disclaimer,
            LineNo = lineNo,
            Employee = employee,
            EmployeeId = employeeId,
            TaxCode = taxCode,
            TaxBase = taxBase,
            Amount = amount,
            Total = total,
        };

    private static string DateText(DateTime value)
        => value.Year >= 1902 ? value.ToString("yyyy-MM-dd") : "";
}
