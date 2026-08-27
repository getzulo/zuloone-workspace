using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Покрытие Costing: оприходование заказа поставщику наполняет регистр стоимости
// запасов (Value = Σ количество × цена, Qty = Σ количество); средняя
// себестоимость товара = Value / Qty.
public class InventoryValueFlowTest : IntegrationTestScriptBase
{
    private async Task<(Guid Location, Guid Item, Guid Supplier)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-COST-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?> { ["Code"] = "WH", ["Name"] = "Warehouse" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?> { ["Name"] = "Main", ["LegalEntity"] = le, ["DivisionType"] = dt });
        var wh = await Db.InsertAsync("Warehouse", new Dictionary<string, object?> { ["Name"] = "Central", ["Division"] = div });
        var lt = await Db.InsertAsync("LocationType", new Dictionary<string, object?> { ["Code"] = "STG", ["Name"] = "Storage" });
        var loc = await Db.InsertAsync("WarehouseLocation", new Dictionary<string, object?> { ["Warehouse"] = wh, ["Name"] = "A-01", ["LocationType"] = lt });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS" });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?> { ["Code"] = "MERCH", ["Name"] = "Merchandise" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Widget", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom });
        var supplier = await Db.InsertAsync("Supplier", new Dictionary<string, object?> { ["Name"] = "Bolt Supply Co" });

        return ((Guid)loc, (Guid)item, (Guid)supplier);
    }

    [IntegrationTest("Оприходование заказа наполняет стоимость запасов; средняя = Value/Qty")]
    public async Task ReceiptFillsInventoryValue()
    {
        var s = await SetupAsync();

        var po = await Db.CreateDocumentAsync("PurchaseOrder",
            new Dictionary<string, object?> { ["Supplier"] = s.Supplier, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = 10m, ["UnitPrice"] = 7m } } });
        await Db.ChangeSubtypeAsync("PurchaseOrder", po, "Received");

        // InventoryValue несёт одну динамическую аналитику (Item) — баланс
        // схлопывается в одну строку; суммируем оба ресурса.
        decimal value = 0m, qty = 0m;
        foreach (var r in await Db.QueryBalancesAsync("InventoryValue"))
        {
            value += Convert.ToDecimal(r["Value"]);
            qty += Convert.ToDecimal(r["Qty"]);
        }
        Assert.IsTrue(value == 70m, "стоимость 10 × 7 = 70, факт {0}", value);
        Assert.IsTrue(qty == 10m, "количество 10, факт {0}", qty);
        Assert.IsTrue(qty > 0m && value / qty == 7m, "средняя себестоимость 7, факт {0}", qty > 0m ? value / qty : -1m);
    }
}
