using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Покрытие Costing FIFO: приход создаёт слои себестоимости, расход списывает по
// старейшим лотам (FIFO), движок отклоняет перерасход слоёв.
public class ItemCostFifoTest : IntegrationTestScriptBase
{
    private async Task<(Guid Location, Guid Item, Guid Supplier)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-FIFO-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?> { ["Code"] = "WH", ["Name"] = "Warehouse" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?> { ["Name"] = "Main", ["LegalEntity"] = le, ["DivisionType"] = dt });
        var wh = await Db.InsertAsync("Store", new Dictionary<string, object?> { ["Name"] = "Central", ["Division"] = div, ["IsSimple"] = true });
        var whZone = await Db.InsertAsync("StoreZone", new Dictionary<string, object?> { ["Name"] = "Зона", ["Store"] = wh, ["IsBarcodeTracking"] = false });
        var lt = await Db.InsertAsync("StoreCellType", new Dictionary<string, object?> {["Code"] = $"STG-{Db.NewId():N}"[..12], ["Name"] = "Storage" });
        var loc = await Db.InsertAsync("StoreCell", new Dictionary<string, object?> { ["Name"] = "A-01", ["Type"] = lt, ["StoreZone"] = whZone, ["RackNumber"] = 1, ["ShelfNumber"] = 1, ["LineNumber"] = 1, ["CellNumber"] = 1 });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS" });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?> { ["Code"] = $"MERCH-{Db.NewId():N}"[..12], ["Name"] = "Merchandise" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Widget", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom });
        var supplier = await Db.InsertAsync("Supplier", new Dictionary<string, object?> { ["Name"] = "Bolt Supply Co" });

        return ((Guid)loc, (Guid)item, (Guid)supplier);
    }

    private async Task ReceiveAsync((Guid Location, Guid Item, Guid Supplier) s, decimal qty, decimal price)
    {
        var po = await Db.CreateDocumentAsync("PurchaseOrder",
            new Dictionary<string, object?> { ["Supplier"] = s.Supplier, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = qty, ["UnitPrice"] = price } } });
        await Db.ChangeSubtypeAsync("PurchaseOrder", po, "Received");
    }

    private async Task<(decimal Qty, decimal Amount)> FifoBalanceAsync(Guid item)
    {
        var rows = await Db.QueryBalancesAsync("ItemCostFifo", "[Item] = '" + item + "'");
        decimal q = 0m, a = 0m;
        foreach (var r in rows) { q += Convert.ToDecimal(r["Quantity"]); a += Convert.ToDecimal(r["Amount"]); }
        return (q, a);
    }

    [IntegrationTest("FIFO: расход списывает старейшие лоты, остаток по FIFO-цене")]
    public async Task IssueConsumesOldestLots()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);   // лот 1: 10 шт по 7 = 70
        await ReceiveAsync(s, 10m, 9m);   // лот 2: 10 шт по 9 = 90

        var afterReceipts = await FifoBalanceAsync(s.Item);
        Assert.IsTrue(afterReceipts.Qty == 20m, "после прихода 20 шт, факт {0}", afterReceipts.Qty);
        Assert.IsTrue(afterReceipts.Amount == 160m, "после прихода стоимость 160, факт {0}", afterReceipts.Amount);

        // Расход 15 шт: FIFO снимает 10×7 + 5×9 = 115. Остаток 5 шт по 9 = 45.
        await Db.PostMovementAsync("ItemCostFifo", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Quantity"] = -15m, ["Amount"] = 0m });

        var afterIssue = await FifoBalanceAsync(s.Item);
        Assert.IsTrue(afterIssue.Qty == 5m, "остаток 5 шт, факт {0}", afterIssue.Qty);
        Assert.IsTrue(afterIssue.Amount == 45m, "остаток по FIFO 45 (5×9), факт {0} (среднее дало бы 40)", afterIssue.Amount);
    }

    [IntegrationTest("FIFO: перерасход слоёв отклоняется движком")]
    public async Task OverdrawRejected()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 5m, 7m);   // всего 5 шт

        var rejected = false;
        try
        {
            await Db.PostMovementAsync("ItemCostFifo", DateTime.UtcNow.Date,
                new Dictionary<string, object?> { ["Item"] = s.Item },
                new Dictionary<string, decimal> { ["Quantity"] = -6m, ["Amount"] = 0m });
        }
        catch (Exception)
        {
            rejected = true;
        }
        Assert.IsTrue(rejected, "расход 6 шт при 5 в наличии должен быть отклонён FIFO-движком");
    }
}
