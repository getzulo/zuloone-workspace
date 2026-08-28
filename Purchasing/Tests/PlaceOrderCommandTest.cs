using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Покрытие команды «Заказать» (Draft → Ordered) и того, что новое промежуточное
// состояние не сломало приход: из Ordered документ по-прежнему переводится в
// Received и проводит движения склада.
public class PlaceOrderCommandTest : IntegrationTestScriptBase
{
    private async Task<(Guid Location, Guid Item, Guid Supplier)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-ORD-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?> { ["Code"] = "WH", ["Name"] = "Warehouse" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?> { ["Name"] = "Main", ["LegalEntity"] = le, ["DivisionType"] = dt });
        var wh = await Db.InsertAsync("Store", new Dictionary<string, object?> { ["Name"] = "Central", ["Division"] = div, ["IsSimple"] = true });
        var whZone = await Db.InsertAsync("StoreZone", new Dictionary<string, object?> { ["Name"] = "Зона", ["Store"] = wh, ["IsBarcodeTracking"] = false });
        var lt = await Db.InsertAsync("StoreCellType", new Dictionary<string, object?> {["Code"] = $"RCV-{Db.NewId():N}"[..12], ["Name"] = "Receiving" });
        var loc = await Db.InsertAsync("StoreCell", new Dictionary<string, object?> { ["Name"] = "R-01", ["Type"] = lt, ["StoreZone"] = whZone, ["RackNumber"] = 1, ["ShelfNumber"] = 1, ["LineNumber"] = 1, ["CellNumber"] = 1 });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS" });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?> { ["Code"] = "RAW", ["Name"] = "Raw material" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Bolt", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom, ["IsRawMaterial"] = true });
        var supplier = await Db.InsertAsync("Supplier", new Dictionary<string, object?> { ["Name"] = "Bolt Supply Co" });

        return ((Guid)loc, (Guid)item, (Guid)supplier);
    }

    [IntegrationTest("Команда «Заказать» переводит заполненный заказ в Ordered")]
    public async Task PlacesFilledOrder()
    {
        var s = await SetupAsync();
        var order = await Db.CreateDocumentAsync("PurchaseOrder",
            new Dictionary<string, object?> { ["Supplier"] = s.Supplier, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = 4m, ["UnitPrice"] = 3m } } },
            subtype: "Draft");

        var commandId = await Db.FindCommandIdAsync("document", "PlaceOrder");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, (Guid)order);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        var doc = await Db.GetAsync("PurchaseOrder", (Guid)order);
        Assert.IsTrue(Convert.ToString(doc!["Subtype"]) == "Ordered",
            "подтип стал Ordered, факт {0}", Convert.ToString(doc["Subtype"]));

        // «Заказано» — это обязательство, а не приход: движений склада быть НЕ должно.
        // Проверка не формальная: две складские проводки закупки привязаны в
        // метаданных к ДОКУМЕНТУ, а не к подтипу Received, поэтому важно убедиться,
        // что появление промежуточного состояния не начало приходовать товар раньше
        // времени.
        decimal atOrdered = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Stock", $"[Cell] = '{s.Location}'"))
            atOrdered += Convert.ToDecimal(r["Qty"]);
        Assert.IsTrue(atOrdered == 0m, "на «Заказано» склад не двигается, факт {0}", atOrdered);

        // Из нового состояния приход по-прежнему проводится и двигает склад.
        await Db.ChangeSubtypeAsync("PurchaseOrder", (Guid)order, "Received");
        decimal onHand = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Stock", $"[Cell] = '{s.Location}'"))
            onHand += Convert.ToDecimal(r["Qty"]);
        Assert.IsTrue(onHand == 4m, "после прихода на ячейке 4, факт {0}", onHand);

        // Провенанс: движение помнит, какой ДОКУМЕНТ его породил.
        var moves = await Db.QueryMovementsAsync("Stock", $"[Cell] = '{s.Location}'");
        Assert.IsTrue(moves.Count > 0, "движения прихода записаны");
        Assert.IsTrue(moves.All(m => Convert.ToString(m["DocumentMetaId"]) == order.ToString()),
            "каждое движение ссылается на заказ-источник");
    }

    [IntegrationTest("Команда «Заказать» отклоняет пустой заказ")]
    public async Task RejectsEmptyOrder()
    {
        var s = await SetupAsync();
        var order = await Db.CreateDocumentAsync("PurchaseOrder",
            new Dictionary<string, object?> { ["Supplier"] = s.Supplier, ["Location"] = s.Location },
            null, subtype: "Draft");

        var commandId = await Db.FindCommandIdAsync("document", "PlaceOrder");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, (Guid)order);

        var doc = await Db.GetAsync("PurchaseOrder", (Guid)order);
        Assert.IsTrue(Convert.ToString(doc!["Subtype"]) == "Draft",
            "пустой заказ остаётся черновиком, факт {0}", Convert.ToString(doc["Subtype"]));
        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("пуст"),
            "пользователь получил причину отказа: {0}", string.Join("; ", run.ClientMessages));
    }
}
