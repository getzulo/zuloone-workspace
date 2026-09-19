#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Social insurance payment to the fund. Mirror of PayrollPaymentXr, not sales pricing.
public partial class SocialInsurancePaymentXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row("", "", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m, 0m, 0m, "", ""));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var doc = await documents.GetDocumentAsync<SocialInsurancePayment>(context.RecordId);
        if (doc == null)
        {
            table.Add(Row("", "", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m, 0m, 0m, "", ""));
            return table;
        }

        var display = context.GetService<IReferenceDisplay>();
        var ct = context.CancellationToken;
        var division = await NameAsync(display, "Division", doc.Division, ct);

        decimal employeeTotal = 0m;
        decimal employerTotal = 0m;
        foreach (var line in doc.Lines)
        {
            employeeTotal += line.EmployeeContribution;
            employerTotal += line.EmployerContribution;
        }
        var total = employeeTotal + employerTotal;

        var n = 0;
        foreach (var line in doc.Lines)
        {
            n++;
            var employee = await NameAsync(display, "Employee", line.Employee, ct);
            var lineTotal = line.EmployeeContribution + line.EmployerContribution;
            table.Add(Row(
                doc.ID ?? "", DateText(doc.DocumentDate), "", "", division, "", "", "", "",
                employeeTotal, employerTotal, total, n, "", 0m, "",
                0m, lineTotal, line.EmployeeContribution, line.EmployerContribution, employee, ""));
        }

        if (n == 0)
        {
            table.Add(Row(
                doc.ID ?? "", DateText(doc.DocumentDate), "", "", division, "", "", "", "",
                employeeTotal, employerTotal, total, 0, "", 0m, "",
                0m, 0m, 0m, 0m, "", ""));
        }

        return table;
    }

    private static object Row(
        string number, string documentDate, string customer, string supplier, string seller,
        string contract, string outlet, string location, string notes,
        decimal subTotal, decimal taxAmount, decimal total,
        int lineNo, string item, decimal quantity, string unit,
        decimal unitPrice, decimal amount, decimal debit, decimal credit,
        string lineParty, string lineNote)
        => new
        {
            Number = number, DocumentDate = documentDate, Customer = customer, Supplier = supplier,
            Seller = seller, Contract = contract, Outlet = outlet, Location = location, Notes = notes,
            SubTotal = subTotal, TaxAmount = taxAmount, Total = total, LineNo = lineNo, Item = item,
            Quantity = quantity, Unit = unit, UnitPrice = unitPrice, Amount = amount,
            Debit = debit, Credit = credit, LineParty = lineParty, LineNote = lineNote,
        };

    private static async Task<string> NameAsync(
        IReferenceDisplay display, string dictionary, Guid? id, CancellationToken ct)
    {
        if (id is not Guid g || g == Guid.Empty) return "";
        return await display.FormatAsync(dictionary, g, ct) ?? "";
    }

    private static string DateText(DateTime value)
        => value.Year >= 1902 ? value.ToString("yyyy-MM-dd") : "";
}
