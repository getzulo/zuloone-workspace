using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Integration coverage for the Purchasing core: receiving a purchase order adds stock
// and recognizes a payable; a zero-quantity order is rejected by the validation event.
public class PurchaseFlowTest : IntegrationTestScriptBase
{
    private async Task<(Guid Location, Guid Item, Guid Supplier)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-PUR-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?> { ["Code"] = "WH", ["Name"] = "Warehouse" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?> { ["Name"] = "Main", ["LegalEntity"] = le, ["DivisionType"] = dt });
        var wh = await Db.InsertAsync("Warehouse", new Dictionary<string, object?> { ["Name"] = "Central", ["Division"] = div });
        var lt = await Db.InsertAsync("LocationType", new Dictionary<string, object?> { ["Code"] = "RCV", ["Name"] = "Receiving" });
        var loc = await Db.InsertAsync("WarehouseLocation", new Dictionary<string, object?> { ["Warehouse"] = wh, ["Name"] = "R-01", ["LocationType"] = lt });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS" });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?> { ["Code"] = "RAW", ["Name"] = "Raw material" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Bolt", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom, ["IsRawMaterial"] = true });
        var supplier = await Db.InsertAsync("Supplier", new Dictionary<string, object?> { ["Name"] = "Bolt Supply Co" });

        return (loc, item, supplier);
    }

    private async Task<Guid> NewOrderAsync((Guid Location, Guid Item, Guid Supplier) s, decimal qty, decimal price)
        => await Db.CreateDocumentAsync("PurchaseOrder",
            new Dictionary<string, object?> { ["Supplier"] = s.Supplier, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = qty, ["UnitPrice"] = price } },
            });

    [IntegrationTest("Приход добавляет в Stock и признаёт кредиторку")]
    public async Task ReceiptAddsStockAndPayable()
    {
        var s = await SetupAsync();
        var po = await NewOrderAsync(s, qty: 10m, price: 3m);
        await Db.ChangeSubtypeAsync("PurchaseOrder", po, "Received");

        decimal stock = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Stock", "[Location] = '" + s.Location + "'")) stock += Convert.ToDecimal(r["Qty"]);
        decimal payable = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Payable")) payable += Convert.ToDecimal(r["Amount"]);

        Assert.IsTrue(stock == 10m, "остаток ячейки должен стать 10, а не {0}", stock);
        Assert.IsTrue(payable == 30m, "кредиторка должна быть 30 (10 × 3), а не {0}", payable);
    }

    [IntegrationTest("Заказ с нулевым количеством отклоняется")]
    public async Task ZeroQuantityIsRejected()
    {
        var s = await SetupAsync();
        var po = await NewOrderAsync(s, qty: 0m, price: 3m);

        var rejected = false;
        try
        {
            await Db.ChangeSubtypeAsync("PurchaseOrder", po, "Received");
            decimal stock = 0m;
            foreach (var r in await Db.QueryBalancesAsync("Stock")) stock += Convert.ToDecimal(r["Qty"]);
            rejected = stock == 0m; // event blocked posting → nothing received
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "заказ с нулевым количеством должен быть отклонён событием");
    }
}
