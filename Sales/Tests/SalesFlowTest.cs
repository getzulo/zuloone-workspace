using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Integration coverage for the Sales core: issuing an invoice ships stock out and
// recognizes revenue; issuing beyond on-hand is rejected by the Stock guard.
public class SalesFlowTest : IntegrationTestScriptBase
{
    private async Task<(Guid Location, Guid Item, Guid Customer)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-SALES-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?> { ["Code"] = "SP", ["Name"] = "SalesPoint" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?> { ["Name"] = "Shop", ["LegalEntity"] = le, ["DivisionType"] = dt });
        var wh = await Db.InsertAsync("Store", new Dictionary<string, object?> { ["Name"] = "Shop WH", ["Division"] = div, ["IsSimple"] = true });
        var whZone = await Db.InsertAsync("StoreZone", new Dictionary<string, object?> { ["Name"] = "Зона", ["Store"] = wh, ["IsBarcodeTracking"] = false });
        var lt = await Db.InsertAsync("StoreCellType", new Dictionary<string, object?> {["Code"] = $"PICK-{Db.NewId():N}"[..12], ["Name"] = "Picking" });
        var loc = await Db.InsertAsync("StoreCell", new Dictionary<string, object?> { ["Name"] = "P-01", ["Type"] = lt, ["StoreZone"] = whZone, ["RackNumber"] = 1, ["ShelfNumber"] = 1, ["LineNumber"] = 1, ["CellNumber"] = 1 });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS" });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?> { ["Code"] = "GOODS", ["Name"] = "Finished goods" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Gadget", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom, ["IsSellable"] = true });
        var customer = await Db.InsertAsync("Customer", new Dictionary<string, object?>
            { ["Name"] = "Buyer Ltd", ["CustomerType"] = "B2B" });

        return (loc, item, customer);
    }

    private async Task StockInAsync(Guid location, Guid item, decimal qty)
    {
        var doc = await Db.CreateDocumentAsync("StockAdjustment",
            new Dictionary<string, object?> { ["Cell"] = location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = item, ["Quantity"] = qty } },
            });
        await Db.ChangeSubtypeAsync("StockAdjustment", doc, "Posted");
    }

    private async Task<Guid> NewInvoiceAsync((Guid Location, Guid Item, Guid Customer) s, decimal qty, decimal price)
        => await Db.CreateDocumentAsync("SalesInvoice",
            new Dictionary<string, object?> { ["Customer"] = s.Customer, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = qty, ["UnitPrice"] = price } },
            });

    [IntegrationTest("Выставление счёта списывает из Stock и признаёт выручку")]
    public async Task IssueShipsAndRecognizesRevenue()
    {
        var s = await SetupAsync();
        await StockInAsync(s.Location, s.Item, 10m);

        var inv = await NewInvoiceAsync(s, qty: 3m, price: 5m);
        await Db.ChangeSubtypeAsync("SalesInvoice", inv, "Issued");

        decimal stock = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Stock", "[Cell] = '" + s.Location + "'")) stock += Convert.ToDecimal(r["Qty"]);
        decimal revenue = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Revenue")) revenue += Convert.ToDecimal(r["Amount"]);

        Assert.IsTrue(stock == 7m, "остаток ячейки должен стать 7 (10 − 3), а не {0}", stock);
        Assert.IsTrue(revenue == 15m, "выручка должна быть 15 (3 × 5), а не {0}", revenue);
    }

    [IntegrationTest("Продажа сверх остатка отклоняется")]
    public async Task OverSellIsRejected()
    {
        var s = await SetupAsync();
        await StockInAsync(s.Location, s.Item, 10m);

        var inv = await NewInvoiceAsync(s, qty: 20m, price: 5m); // only 10 on hand

        var rejected = false;
        try
        {
            await Db.ChangeSubtypeAsync("SalesInvoice", inv, "Issued");
            decimal revenue = 0m;
            foreach (var r in await Db.QueryBalancesAsync("Revenue")) revenue += Convert.ToDecimal(r["Amount"]);
            rejected = revenue == 0m; // posting blocked → no revenue recognized
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "продажа 20 при остатке 10 должна быть отклонена");
    }
}
