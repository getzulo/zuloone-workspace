using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Routing is the operation recipe. Explode snapshots it onto the order.
// Posting still only moves stock. Empty route is lawful.
public class ProductionRoutingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private async Task<Func<string, Task<Item>>> ItemFactoryAsync()
    {
        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PCS-{Db.NewId():N}"[..12];
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"RT-{Db.NewId():N}"[..12];
        group.Name = "Routing";
        group = await DictionaryManager.SaveRecordAsync(group);

        var uomId = uom.MetaId;
        var groupId = group.MetaId;
        return async name =>
        {
            var item = DictionaryManager.NewRecord<Item>();
            item.Name = name;
            item.ItemGroup = groupId;
            item.UnitOfMeasure = uomId;
            return await DictionaryManager.SaveRecordAsync(item);
        };
    }

    private async Task<Guid> NewLocationAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = "EUR";
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "Route GmbH";
        legalEntity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"RT-{Db.NewId():N}"[..12];
        divisionType.Name = "Production";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Цех";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Склад цеха";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"RT-{Db.NewId():N}"[..12];
        cellType.Name = "Production";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.StoreZone = zone.MetaId;
        cell.Type = cellType.MetaId;
        cell.Name = "R-01";
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);
        return cell.MetaId;
    }

    private async Task SetAutoExpandAsync(bool enabled)
    {
        var rows = await DictionaryManager.GetRecordsAsync<ProductionSettings>();
        if (rows.Count == 0)
        {
            var created = DictionaryManager.NewRecord<ProductionSettings>();
            created.AutoExpandBom = enabled;
            await DictionaryManager.SaveRecordAsync(created);
            return;
        }
        foreach (var row in rows)
        {
            row.AutoExpandBom = enabled;
            await DictionaryManager.SaveRecordAsync(row);
        }
    }

    private async Task<(Guid Bom, Guid Product, Guid Location)> SeedBomAsync()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие");
        var comp = await newItem("Сырьё");
        var loc = await NewLocationAsync();
        var bom = DictionaryManager.NewRecord<BillOfMaterials>();
        bom.Name = "BOM";
        bom.Product = product.MetaId;
        bom.OutputQty = 1m;
        bom = await DictionaryManager.SaveRecordAsync(bom);
        var row = DictionaryManager.NewRecord<BomComponent>();
        row.Bom = bom.MetaId;
        row.Component = comp.MetaId;
        row.QtyPer = 1m;
        await DictionaryManager.SaveRecordAsync(row);
        return (bom.MetaId, product.MetaId, loc);
    }

    private async Task<ProductionOrder> DraftAsync(Guid product, Guid location, decimal qty)
    {
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product;
        order.Quantity = qty;
        order.Location = location;
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    [IntegrationTest("Развернуть спецификацию штампует операции маршрута")]
    public async Task ExpandBomStampsOperations()
    {
        await SetAutoExpandAsync(false);
        var seed = await SeedBomAsync();
        var center = DictionaryManager.NewRecord<WorkCenter>();
        center.Name = "Пила";
        center = await DictionaryManager.SaveRecordAsync(center);

        var routing = DictionaryManager.NewRecord<Routing>();
        routing.Name = "Распил";
        routing.Product = seed.Product;
        routing = await DictionaryManager.SaveRecordAsync(routing);

        var cut = DictionaryManager.NewRecord<RoutingOperation>();
        cut.Routing = routing.MetaId;
        cut.Sequence = 10;
        cut.Name = "Распил";
        cut.WorkCenter = center.MetaId;
        cut.SetupMinutes = 5;
        cut.RunMinutesPerUnit = 2m;
        await DictionaryManager.SaveRecordAsync(cut);

        var pack = DictionaryManager.NewRecord<RoutingOperation>();
        pack.Routing = routing.MetaId;
        pack.Sequence = 20;
        pack.Name = "Упаковка";
        pack.SetupMinutes = 0;
        pack.RunMinutesPerUnit = 0.5m;
        await DictionaryManager.SaveRecordAsync(pack);

        var order = await DraftAsync(seed.Product, seed.Location, 10m);
        var seen = await DictionaryManager.GetRecordsAsync<Routing>();
        Assert.IsTrue(seen.Any(r => r.Product == seed.Product && !r.IsDisabled),
            "маршрут виден до команды, записей {0}", seen.Count);

        var stepsSeen = await DictionaryManager.GetRecordsAsync<RoutingOperation>(
            s => s.Routing == routing.MetaId);
        Assert.IsTrue(stepsSeen.Count == 2, "шагов маршрута 2, факт {0}", stepsSeen.Count);

        var commandId = await Db.FindCommandIdAsync("document", "ExpandBom");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(run.Success, "команда: {0}", run.Message ?? "");

        var stamped = await GetService<IRoutingService>().StampOperationsAsync(order.MetaId);
        Assert.IsTrue(stamped == 2, "штамп вернул 2, факт {0}", stamped);

        var raw = await GetService<IDataService>().QueryAsync(
            "TP_ProductionOrderOperations", $"[OwnerMetaId] = '{order.MetaId}'");
        Assert.IsTrue(raw.Count == 2, "строк TP операций 2, факт {0}", raw.Count);

        var full = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
        Assert.IsTrue(full!.Components.Count == 1, "компонент развёрнут, факт {0}", full.Components.Count);

        var bySeq = raw
            .OrderBy(r => Convert.ToInt32(r["Sequence"] ?? 0))
            .ToList();
        Assert.IsTrue(Convert.ToString(bySeq[0]["Name"]) == "Распил",
            "первая операция Распил, факт {0}", bySeq[0]["Name"]);
        Assert.IsTrue(Convert.ToInt32(bySeq[0]["SetupMinutes"] ?? 0) == 5,
            "наладка 5, факт {0}", bySeq[0]["SetupMinutes"]);
        Assert.IsTrue(Convert.ToDecimal(bySeq[0]["RunMinutes"] ?? 0m) == 20m,
            "работа 2×10=20, факт {0}", bySeq[0]["RunMinutes"]);
        Assert.IsTrue(Convert.ToDecimal(bySeq[1]["RunMinutes"] ?? 0m) == 5m,
            "упаковка 0.5×10=5, факт {0}", bySeq[1]["RunMinutes"]);
    }

    [IntegrationTest("Без маршрута операции пустые, спецификация всё равно разворачивается")]
    public async Task NoRoutingLeavesOperationsEmpty()
    {
        await SetAutoExpandAsync(false);
        var seed = await SeedBomAsync();
        var order = await DraftAsync(seed.Product, seed.Location, 3m);
        var commandId = await Db.FindCommandIdAsync("document", "ExpandBom");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(run.Success, "команда: {0}", run.Message ?? "");
        var full = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
        Assert.IsTrue(full!.Components.Count == 1, "компонент есть");
        Assert.IsTrue(full.Operations.Count == 0, "операций нет, факт {0}", full.Operations.Count);
    }

    [IntegrationTest("Отключённый маршрут не штампуется")]
    public async Task DisabledRoutingIsIgnored()
    {
        await SetAutoExpandAsync(false);
        var seed = await SeedBomAsync();
        var routing = DictionaryManager.NewRecord<Routing>();
        routing.Name = "Старый";
        routing.Product = seed.Product;
        routing.IsDisabled = true;
        routing = await DictionaryManager.SaveRecordAsync(routing);
        var step = DictionaryManager.NewRecord<RoutingOperation>();
        step.Routing = routing.MetaId;
        step.Sequence = 10;
        step.Name = "Не должна попасть";
        await DictionaryManager.SaveRecordAsync(step);

        var order = await DraftAsync(seed.Product, seed.Location, 1m);
        var commandId = await Db.FindCommandIdAsync("document", "ExpandBom");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(run.Success, "команда: {0}", run.Message ?? "");
        var full = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
        Assert.IsTrue(full!.Operations.Count == 0, "отключённый маршрут пуст, факт {0}", full.Operations.Count);
    }

    [IntegrationTest("Два шага с одним порядком отклоняются")]
    public async Task DuplicateSequenceRejected()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Дубль");
        var routing = DictionaryManager.NewRecord<Routing>();
        routing.Name = "Дубль";
        routing.Product = product.MetaId;
        routing = await DictionaryManager.SaveRecordAsync(routing);

        var a = DictionaryManager.NewRecord<RoutingOperation>();
        a.Routing = routing.MetaId;
        a.Sequence = 10;
        a.Name = "Первая";
        await DictionaryManager.SaveRecordAsync(a);

        var b = DictionaryManager.NewRecord<RoutingOperation>();
        b.Routing = routing.MetaId;
        b.Sequence = 10;
        b.Name = "Вторая";
        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(b); }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("Порядок"), "дубль порядка отклонён, факт: {0}", reason);
    }
}
