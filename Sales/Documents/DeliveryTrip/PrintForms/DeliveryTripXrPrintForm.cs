#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Route sheet: driver, route, vehicle, depot, stops. No sales money.
public partial class DeliveryTripXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row("", "", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m, 0m, 0m, "", ""));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var doc = await documents.GetDocumentAsync<DeliveryTrip>(context.RecordId);
        if (doc == null)
        {
            table.Add(Row("", "", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m, 0m, 0m, "", ""));
            return table;
        }

        var display = context.GetService<IReferenceDisplay>();
        var ct = context.CancellationToken;
        var driver = await NameAsync(display, "Driver", doc.Driver, ct);
        var route = await NameAsync(display, "DeliveryRoute", doc.Route, ct);
        var vehicle = await NameAsync(display, "Vehicle", doc.Vehicle, ct);
        var depot = await NameAsync(display, "Store", doc.Depot, ct);
        var tripDate = DateText(doc.DeliveryDate);

        var n = 0;
        foreach (var line in doc.Lines)
        {
            n++;
            var stop = line.StopSequence ?? 0;
            if (stop == 0) stop = n;
            var orderText = await NameAsync(display, "SalesOrder", line.SalesOrder, ct);
            var outlet = await NameAsync(display, "CustomerOutlet", line.Outlet, ct);
            table.Add(Row(
                doc.ID ?? "",
                tripDate,
                "",
                "",
                driver,
                route,
                outlet,
                depot,
                vehicle,
                0m,
                0m,
                0m,
                stop,
                orderText,
                line.QtyShipped,
                "",
                0m,
                0m,
                0m,
                0m,
                "",
                line.Outcome == StopOutcome.Unspecified ? "" : line.Outcome.ToString()));
        }

        if (n == 0)
        {
            table.Add(Row(
                doc.ID ?? "",
                tripDate,
                "",
                "",
                driver,
                route,
                "",
                depot,
                vehicle,
                0m,
                0m,
                0m,
                0,
                "",
                0m,
                "",
                0m,
                0m,
                0m,
                0m,
                "",
                ""));
        }

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

    private static string DateText(DateTime value)
        => value.Year >= 1902 ? value.ToString("yyyy-MM-dd") : "";
}
