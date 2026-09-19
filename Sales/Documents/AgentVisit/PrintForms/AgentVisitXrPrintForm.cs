#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Visit slip: customer, outlet, check-in/out, skip reason. No sales money.
public partial class AgentVisitXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row("", "", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m, 0m, 0m, "", ""));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var doc = await documents.GetDocumentAsync<AgentVisit>(context.RecordId);
        if (doc == null)
        {
            table.Add(Row("", "", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m, 0m, 0m, "", ""));
            return table;
        }

        var display = context.GetService<IReferenceDisplay>();
        var ct = context.CancellationToken;
        var customer = await NameAsync(display, "Customer", doc.Customer, ct);
        var outlet = await NameAsync(display, "CustomerOutlet", doc.Outlet, ct);
        var route = await NameAsync(display, "VisitRoute", doc.Route, ct);
        var when = $"{DateTimeText(doc.CheckedInAt)} — {DateTimeText(doc.CheckedOutAt)}".Trim(' ', '—');
        var stop = doc.StopSequence is int seq && seq != 0 ? seq : 0;

        table.Add(Row(
            doc.ID ?? "",
            DateText(doc.DocumentDate),
            customer,
            "",
            "",
            route,
            outlet,
            "",
            doc.SkipReason ?? "",
            0m,
            0m,
            0m,
            stop,
            when,
            0m,
            "",
            0m,
            0m,
            0m,
            0m,
            doc.GeoStatus ?? "",
            ""));

        return table;
    }

    private static object Row(
        string number,
        string documentDate,
        string customer,
        string supplier,
        string seller,
        string contract,
        string outlet,
        string location,
        string notes,
        decimal subTotal,
        decimal taxAmount,
        decimal total,
        int lineNo,
        string item,
        decimal quantity,
        string unit,
        decimal unitPrice,
        decimal amount,
        decimal debit,
        decimal credit,
        string lineParty,
        string lineNote)
        => new
        {
            Number = number,
            DocumentDate = documentDate,
            Customer = customer,
            Supplier = supplier,
            Seller = seller,
            Contract = contract,
            Outlet = outlet,
            Location = location,
            Notes = notes,
            SubTotal = subTotal,
            TaxAmount = taxAmount,
            Total = total,
            LineNo = lineNo,
            Item = item,
            Quantity = quantity,
            Unit = unit,
            UnitPrice = unitPrice,
            Amount = amount,
            Debit = debit,
            Credit = credit,
            LineParty = lineParty,
            LineNote = lineNote,
        };

    private static async Task<string> NameAsync(
        IReferenceDisplay display, string dictionary, Guid? id, CancellationToken ct)
    {
        if (id is not Guid g || g == Guid.Empty) return "";
        return await display.FormatAsync(dictionary, g, ct) ?? "";
    }

    private static string DateText(DateTime? value)
        => value is DateTime d && d.Year >= 1902 ? d.ToString("yyyy-MM-dd") : "";

    private static string DateTimeText(DateTime? value)
        => value is DateTime d && d.Year >= 1902 ? d.ToString("yyyy-MM-dd HH:mm") : "";
}
