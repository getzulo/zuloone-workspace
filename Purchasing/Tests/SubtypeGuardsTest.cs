using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Защита от дурака вокруг подтипов: карта разрешённых переходов и подтип,
// замораживающий данные документа.
//
// Карта переходов у PurchaseOrder задана как Draft → только Ordered: прыгнуть из
// черновика сразу в приход нельзя, сначала надо заказать. Пустая карта у
// остальных подтипов ничего не ограничивает — так старые модели продолжают
// работать без изменений.
public class SubtypeGuardsTest : IntegrationTestScriptBase
{
    private static readonly Guid PurchaseOrderType = Guid.Parse("6935af7d-5f73-45d5-ad4c-d4a21dbe0b67");

    private async Task<(Guid Location, Guid Item, Guid Supplier)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-GRD-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?>
            { ["Code"] = $"WH-{Db.NewId():N}"[..12], ["Name"] = "Warehouse" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?>
            { ["Name"] = "Main", ["LegalEntity"] = le, ["DivisionType"] = dt });
        var store = await Db.InsertAsync("Store", new Dictionary<string, object?>
            { ["Name"] = "Central", ["Division"] = div, ["IsSimple"] = true });
        var zone = await Db.InsertAsync("StoreZone", new Dictionary<string, object?>
            { ["Name"] = "Зона", ["Store"] = store, ["IsBarcodeTracking"] = false });
        var ct = await Db.InsertAsync("StoreCellType", new Dictionary<string, object?>
            { ["Code"] = $"RCV-{Db.NewId():N}"[..12], ["Name"] = "Receiving" });
        var loc = await Db.InsertAsync("StoreCell", new Dictionary<string, object?>
            { ["Name"] = "R-01", ["Type"] = ct, ["StoreZone"] = zone, ["RackNumber"] = 1, ["ShelfNumber"] = 1, ["LineNumber"] = 1, ["CellNumber"] = 1 });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?>
            { ["Name"] = "Piece", ["Code"] = $"PCS-{Db.NewId():N}"[..12] });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?>
            { ["Code"] = $"RAW-{Db.NewId():N}"[..12], ["Name"] = "Raw material" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Bolt", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom, ["IsRawMaterial"] = true });
        var supplier = await Db.InsertAsync("Supplier", new Dictionary<string, object?> { ["Name"] = "Bolt Supply Co" });

        return ((Guid)loc, (Guid)item, (Guid)supplier);
    }

    private async Task<Guid> DraftOrderAsync((Guid Location, Guid Item, Guid Supplier) s)
        => (Guid)await Db.CreateDocumentAsync("PurchaseOrder",
            new Dictionary<string, object?> { ["Supplier"] = s.Supplier, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = 5m, ["UnitPrice"] = 3m } },
            },
            subtype: "Draft");

    [IntegrationTest("Переход по карте разрешён: Draft → Ordered")]
    public async Task AllowedTransitionPasses()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);

        await Db.ChangeSubtypeAsync("PurchaseOrder", order, "Ordered");

        var doc = await Db.GetAsync("PurchaseOrder", order);
        Assert.IsTrue((doc?["Subtype"] as string) == "Ordered",
            "документ должен оказаться в Ordered, факт {0}", doc?["Subtype"]);
    }

    [IntegrationTest("Переход вне карты отклоняется с указанием допустимых")]
    public async Task DisallowedTransitionRejected()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);

        var reason = "";
        try { await Db.ChangeSubtypeAsync("PurchaseOrder", order, "Received"); }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Length > 0, "прыжок Draft → Received должен быть отклонён картой переходов");
        // Причина обязана называть допустимые цели — иначе тест зеленел бы от
        // любой поломки внутри проведения.
        Assert.IsTrue(reason.Contains("не разрешён"), "отказ должен быть от карты переходов, факт: {0}", reason);

        var doc = await Db.GetAsync("PurchaseOrder", order);
        Assert.IsTrue((doc?["Subtype"] as string) == "Draft",
            "отклонённый переход не двигает документ, факт {0}", doc?["Subtype"]);
    }

    [IntegrationTest("Пустая карта ничего не ограничивает")]
    public async Task EmptyMapAllowsEverything()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);
        await Db.ChangeSubtypeAsync("PurchaseOrder", order, "Ordered");

        // У Ordered карта не задана — значит из него можно куда угодно.
        await Db.ChangeSubtypeAsync("PurchaseOrder", order, "Received");

        var doc = await Db.GetAsync("PurchaseOrder", order);
        Assert.IsTrue((doc?["Subtype"] as string) == "Received",
            "из подтипа без карты переход свободен, факт {0}", doc?["Subtype"]);
    }

    [IntegrationTest("В запертом подтипе строку изменить нельзя")]
    public async Task LockedSubtypeRejectsLineEdit()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);
        await Db.ChangeSubtypeAsync("PurchaseOrder", order, "Ordered");
        await Db.ChangeSubtypeAsync("PurchaseOrder", order, "Received");

        var rows = await Db.QueryAsync("TP_PurchaseOrderLines", $"OwnerMetaId = '{order}'");
        Assert.IsTrue(rows.Count == 1, "строка должна быть одна, факт {0}", rows.Count);

        var reason = "";
        try
        {
            await Db.UpdateAsync("TP_PurchaseOrderLines", (Guid)rows[0]["MetaId"],
                new Dictionary<string, object?> { ["Quantity"] = 999m });
        }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Contains("только для чтения"),
            "правка строки в подтипе Received должна быть отклонена, факт: {0}", reason);

        var after = await Db.QueryAsync("TP_PurchaseOrderLines", $"OwnerMetaId = '{order}'");
        Assert.IsTrue(Convert.ToDecimal(after[0]["Quantity"]) == 5m,
            "количество не изменилось, факт {0}", after[0]["Quantity"]);
    }

    [IntegrationTest("В незапертом подтипе строка правится свободно")]
    public async Task UnlockedSubtypeAllowsLineEdit()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);

        var rows = await Db.QueryAsync("TP_PurchaseOrderLines", $"OwnerMetaId = '{order}'");
        await Db.UpdateAsync("TP_PurchaseOrderLines", (Guid)rows[0]["MetaId"],
            new Dictionary<string, object?> { ["Quantity"] = 7m });

        var after = await Db.QueryAsync("TP_PurchaseOrderLines", $"OwnerMetaId = '{order}'");
        Assert.IsTrue(Convert.ToDecimal(after[0]["Quantity"]) == 7m,
            "в Draft правка проходит, факт {0}", after[0]["Quantity"]);
    }

    [IntegrationTest("Из запертого подтипа документ всё ещё можно вывести")]
    public async Task LockedSubtypeStillAllowsTransitionOut()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);
        await Db.ChangeSubtypeAsync("PurchaseOrder", order, "Ordered");
        await Db.ChangeSubtypeAsync("PurchaseOrder", order, "Received");

        // Блокировка данных не должна запирать сам документ: иначе из Received
        // не было бы выхода вообще.
        await Db.ChangeSubtypeAsync("PurchaseOrder", order, "Ordered");

        var doc = await Db.GetAsync("PurchaseOrder", order);
        Assert.IsTrue((doc?["Subtype"] as string) == "Ordered",
            "выход из запертого подтипа разрешён, факт {0}", doc?["Subtype"]);
    }
}
