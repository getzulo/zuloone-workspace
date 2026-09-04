using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Автоотбор: задание появляется от ПОДТВЕРЖДЁННОГО заказа, не от счёта.
// Счёт уже списывает — порождать отбор из него некому. Зеркало раскладки
// у прихода: черновик, идемпотентно по графу, только при включённой дисциплине.
public class AutoPickFromOrderTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();
    private static ISalesFulfillmentService Fulfillment => GetService<ISalesFulfillmentService>();

    private sealed class Yard
    {
        public Guid Receiving;
        public Guid Storage;
        public Guid Picking;
        public Guid Item;
        public Guid Customer;
    }

    private async Task<Yard> SetupAsync()
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
        legalEntity.Name = "ACME GmbH";
        legalEntity.RegistrationNumber = $"REG-AP-{Db.NewId():N}"[..18];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"AP-{Db.NewId():N}"[..12];
        divisionType.Name = "Warehouse";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Central";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PCS-{Db.NewId():N}"[..12];
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"GOODS-{Db.NewId():N}"[..12];
        group.Name = "Goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        return new Yard
        {
            Receiving = await NewCellAsync(zone.MetaId, StoreCellPurpose.Receiving, "R-01", 1),
            Storage = await NewCellAsync(zone.MetaId, StoreCellPurpose.Storage, "S-01", 2),
            Picking = await NewCellAsync(zone.MetaId, StoreCellPurpose.Picking, "P-01", 3),
            Item = item.MetaId,
            Customer = customer.MetaId,
        };
    }

    private async Task<Guid> NewCellAsync(Guid zone, StoreCellPurpose purpose, string name, int number)
    {
        var type = DictionaryManager.NewRecord<StoreCellType>();
        type.Code = $"{purpose}-{Db.NewId():N}"[..12];
        type.Name = purpose.ToString();
        type.Purpose = purpose;
        type = await DictionaryManager.SaveRecordAsync(type);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = name;
        cell.Type = type.MetaId;
        cell.StoreZone = zone;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = number;
        return (await DictionaryManager.SaveRecordAsync(cell)).MetaId;
    }

    private static async Task SetDisciplineAsync(bool on)
    {
        var manager = GetService<IDictionaryManager<InventorySettings>>();
        var rows = await manager.GetRecordsAsync("1 = 1");
        var settings = rows.Count > 0 ? rows[0] : await manager.NewRecordAsync();
        settings.EnforceWarehouseTasks = on;
        await manager.SaveRecordAsync(settings);
    }

    private static Task SeedAsync(Guid cell, Guid item, decimal qty)
        => TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item },
            new Dictionary<string, decimal> { ["Qty"] = qty });

    private static async Task<SalesOrder> NewOrderAsync(Yard y, Guid location, decimal qty)
    {
        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = y.Customer;
        order.Location = location;
        order.DeliveryDate = DateTime.UtcNow.Date.AddDays(1);
        order.Lines.Add(new SalesOrderLinesTablePartRow { Item = y.Item, Quantity = qty, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    private async Task RunCommandAsync(string name, Guid documentId)
    {
        var commandId = await Db.FindCommandIdAsync("document", name);
        var run = await Db.ExecuteDocumentCommandAsync(commandId, documentId);
        Assert.IsTrue(run.Success, "команда {0}: {1}", name, run.Message ?? string.Join("; ", run.ClientMessages));
    }

    private static async Task<PickTask> SinglePickAsync(Guid orderId)
    {
        var family = await DocumentManager.GetDocumentFamilyAsync(orderId);
        var tasks = family.Nodes.Where(n => n.DocTypeName == "PickTask").ToList();
        Assert.IsTrue(tasks.Count == 1, "заданий отбора ровно одно, факт {0}", tasks.Count);
        var task = await DocumentManager.GetDocumentAsync<PickTask>(tasks[0].DocId);
        Assert.IsNotNull(task, "задание читается");
        return task!;
    }

    [IntegrationTest("Дисциплина выключена: подтверждение заданий не плодит")]
    public async Task DisciplineOffSpawnsNoPick()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(false);
        await SeedAsync(y.Picking, y.Item, 8m);

        var order = await NewOrderAsync(y, y.Picking, 3m);
        await RunCommandAsync("ConfirmSalesOrder", order.MetaId);

        var family = await DocumentManager.GetDocumentFamilyAsync(order.MetaId);
        Assert.IsTrue(!family.Nodes.Any(n => n.DocTypeName == "PickTask"),
            "без дисциплины отбор не порождается");
    }

    [IntegrationTest("Подтверждение заказа заводит черновик отбора хранение→отбор")]
    public async Task ConfirmSpawnsDraftPickFromStorage()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SeedAsync(y.Storage, y.Item, 10m);

        Assert.IsTrue(await Fulfillment.AvailableQtyAsync(y.Picking, y.Item) == 10m,
            "свободно по складу 10, хотя в отборе пусто, факт {0}",
            await Fulfillment.AvailableQtyAsync(y.Picking, y.Item));

        var order = await NewOrderAsync(y, y.Picking, 4m);
        await RunCommandAsync("ConfirmSalesOrder", order.MetaId);

        var task = await SinglePickAsync(order.MetaId);
        Assert.IsTrue(task.Subtype == PickTask.Subtypes.Draft,
            "черновик — товар ещё не переставили, факт {0}", task.Subtype);
        Assert.IsTrue(task.FromCell == y.Storage, "забирать из хранения");
        Assert.IsTrue(task.Lines.Count == 1 && task.Lines[0].ToCell == y.Picking && task.Lines[0].Quantity == 4m,
            "строка 4 в ячейку отбора заказа");
    }

    [IntegrationTest("Повтор подтверждения не плодит второе задание отбора")]
    public async Task RepostDoesNotDuplicatePick()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SeedAsync(y.Storage, y.Item, 6m);

        var order = await NewOrderAsync(y, y.Picking, 2m);
        await RunCommandAsync("ConfirmSalesOrder", order.MetaId);
        var first = await SinglePickAsync(order.MetaId);

        var again = await Fulfillment.EnsurePickTaskAsync(order.MetaId);
        Assert.IsTrue(again == first.MetaId,
            "повтор вернул то же задание: было {0}, стало {1}", first.MetaId, again);

        order.Subtype = SalesOrder.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(order);
        var after = await SinglePickAsync(order.MetaId);
        Assert.IsTrue(after.MetaId == first.MetaId,
            "перепроведение не завело второе: было {0}, стало {1}", first.MetaId, after.MetaId);
    }

    [IntegrationTest("Свободный остаток при дисциплине — по складу: второй заказ сверх хранения отклонён")]
    public async Task ConfirmBeyondStoreFreeIsRejected()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SeedAsync(y.Storage, y.Item, 5m);

        var first = await NewOrderAsync(y, y.Picking, 4m);
        await RunCommandAsync("ConfirmSalesOrder", first.MetaId);

        var second = await NewOrderAsync(y, y.Picking, 3m);
        var confirmId = await Db.FindCommandIdAsync("document", "ConfirmSalesOrder");
        var run = await Db.ExecuteDocumentCommandAsync(confirmId, second.MetaId);
        var after = await DocumentManager.GetDocumentAsync<SalesOrder>(second.MetaId);

        Assert.IsTrue(after!.Subtype == SalesOrder.Subtypes.Draft,
            "второй остаётся черновиком, факт {0}", after.Subtype ?? "<null>");
        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("остатка") || !run.Success,
            "пользователь видит отказ: {0}", string.Join("; ", run.ClientMessages));
    }

    [IntegrationTest("При дисциплине заказ из ячейки хранения не подтверждается")]
    public async Task ConfirmFromStorageCellIsRejected()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SeedAsync(y.Storage, y.Item, 8m);

        var order = await NewOrderAsync(y, y.Storage, 2m);
        var confirmId = await Db.FindCommandIdAsync("document", "ConfirmSalesOrder");
        var run = await Db.ExecuteDocumentCommandAsync(confirmId, order.MetaId);
        var after = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);

        Assert.IsTrue(after!.Subtype == SalesOrder.Subtypes.Draft,
            "заказ из хранения остаётся черновиком, факт {0}", after.Subtype ?? "<null>");
        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("ОТБОРА") || !run.Success,
            "отказ про ячейку отбора: {0}", string.Join("; ", run.ClientMessages));
    }
}
