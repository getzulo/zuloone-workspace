using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// АДРЕСНАЯ СКЛАДСКАЯ ДИСЦИПЛИНА.
//
// Задания раскладки и отбора существовали давно и двигали склад, но их никто не
// требовал: и приход, и отгрузка указывали ЛЮБУЮ ячейку. Роли ячеек
// (приёмка/хранение/отбор) жили только в именах тестовых фикстур. Теперь роль —
// поле метаданных StoreCellType.Purpose, а требование ходить по цепочке
// включается флагом InventorySettings.EnforceWarehouseTasks.
//
// Флаг ВЫКЛЮЧЕН по умолчанию, и это не осторожность ради осторожности: сегодня
// около тридцати тестов кладут товар в ячейки произвольного назначения, а часть
// производственных вообще использует выдуманные id ячеек. Поэтому здесь
// проверяется ОБА мира — включённый и выключенный, — и второй важнее первого.
public class WarehouseTaskFlowTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Yard
    {
        public Guid Store;
        public Guid Receiving;
        public Guid Storage;
        public Guid Picking;
        public Guid Item;
        public Guid Customer;
        public Guid Supplier;
    }

    // ───────────────────────────── мастер-данные ─────────────────────────────

    /// <summary>Склад с тремя ячейками РАЗНОГО назначения. Назначение задаётся
    /// полем Purpose, а не именем типа: на имя полагаться нельзя — старые фикстуры
    /// называют ячейку «Storage» и принимают в неё товар.</summary>
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
        legalEntity.RegistrationNumber = $"REG-WMS-{Db.NewId():N}"[..18];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"WH-{Db.NewId():N}"[..12];
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

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        return new Yard
        {
            Store = store.MetaId,
            Receiving = await NewCellAsync(zone.MetaId, StoreCellPurpose.Receiving, "R-01", 1),
            Storage = await NewCellAsync(zone.MetaId, StoreCellPurpose.Storage, "S-01", 2),
            Picking = await NewCellAsync(zone.MetaId, StoreCellPurpose.Picking, "P-01", 3),
            Item = item.MetaId,
            Customer = customer.MetaId,
            Supplier = supplier.MetaId,
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

    /// <summary>Флаг дисциплины — настройка модуля, одна запись на стенд.</summary>
    private static async Task SetDisciplineAsync(bool on)
    {
        var manager = GetService<IDictionaryManager<InventorySettings>>();
        var rows = await manager.GetRecordsAsync("1 = 1");
        var settings = rows.Count > 0 ? rows[0] : await manager.NewRecordAsync();
        settings.EnforceWarehouseTasks = on;
        await manager.SaveRecordAsync(settings);
    }

    // ─────────────────────────────── операции ────────────────────────────────

    private static Task<decimal> OnHandAsync(Guid cell, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item });

    private static Task SeedAsync(Guid cell, Guid item, decimal qty)
        => TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item },
            new Dictionary<string, decimal> { ["Qty"] = qty });

    private static async Task<PutAwayTask> PutAwayAsync(Guid from, Guid to, Guid item, decimal qty)
    {
        var doc = await DocumentManager.NewDocumentAsync<PutAwayTask>();
        doc.FromCell = from;
        doc.Lines.Add(new PutAwayTaskLinesTablePartRow { Item = item, Quantity = qty, ToCell = to });
        await DocumentManager.SaveDocumentAsync(doc);

        doc.Subtype = PutAwayTask.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }

    private static async Task<PickTask> PickAsync(Guid from, Guid to, Guid item, decimal qty)
    {
        var doc = await DocumentManager.NewDocumentAsync<PickTask>();
        doc.FromCell = from;
        doc.Lines.Add(new PickTaskLinesTablePartRow { Item = item, Quantity = qty, ToCell = to });
        await DocumentManager.SaveDocumentAsync(doc);

        doc.Subtype = PickTask.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }

    private static async Task SellAsync(Yard y, Guid cell, decimal qty)
    {
        var inv = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        inv.Customer = y.Customer;
        inv.Location = cell;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = y.Item, Quantity = qty, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(inv);

        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);
    }

    private static async Task<PurchaseOrder> ReceiveAsync(Yard y, Guid cell, decimal qty)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = y.Supplier;
        order.Location = cell;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = y.Item, Quantity = qty, UnitPrice = 7m });
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    /// <summary>Проведение обязано быть отклонено. Отказ приезжает исключением —
    /// текст проверяем на ключевое слово, чтобы не поймать ЧУЖУЮ ошибку и не
    /// принять её за сработавшую защиту.</summary>
    private static async Task<string> RejectedAsync(Func<Task> action, string because)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        Assert.IsTrue(false, "проведение обязано быть отклонено: {0}", because);
        return string.Empty;
    }

    // ─────────────────────────────── сценарии ────────────────────────────────

    [IntegrationTest("Дисциплина включена: приёмка → хранение → отбор → отгрузка")]
    public async Task FullCycleUnderDiscipline()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);

        await SeedAsync(y.Receiving, y.Item, 10m);

        await PutAwayAsync(y.Receiving, y.Storage, y.Item, 10m);
        Assert.IsTrue(await OnHandAsync(y.Receiving, y.Item) == 0m,
            "приёмка опустела, факт {0}", await OnHandAsync(y.Receiving, y.Item));
        Assert.IsTrue(await OnHandAsync(y.Storage, y.Item) == 10m,
            "на хранении 10, факт {0}", await OnHandAsync(y.Storage, y.Item));

        await PickAsync(y.Storage, y.Picking, y.Item, 4m);
        Assert.IsTrue(await OnHandAsync(y.Storage, y.Item) == 6m,
            "на хранении осталось 6, факт {0}", await OnHandAsync(y.Storage, y.Item));
        Assert.IsTrue(await OnHandAsync(y.Picking, y.Item) == 4m,
            "в отборе 4, факт {0}", await OnHandAsync(y.Picking, y.Item));

        await SellAsync(y, y.Picking, 4m);
        Assert.IsTrue(await OnHandAsync(y.Picking, y.Item) == 0m,
            "отгрузка забрала из отбора всё, факт {0}", await OnHandAsync(y.Picking, y.Item));
        // Хранение продажа не трогает: товар уходит строго из ячейки отбора.
        Assert.IsTrue(await OnHandAsync(y.Storage, y.Item) == 6m,
            "хранение не тронуто, факт {0}", await OnHandAsync(y.Storage, y.Item));
    }

    [IntegrationTest("Дисциплина включена: задание из чужой ячейки и в чужую ячейку отклоняется")]
    public async Task WrongCellsAreRejectedUnderDiscipline()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);

        await SeedAsync(y.Receiving, y.Item, 10m);
        await SeedAsync(y.Storage, y.Item, 10m);

        var m1 = await RejectedAsync(() => PutAwayAsync(y.Storage, y.Storage, y.Item, 1m),
            "раскладка забирает не из приёмки");
        Assert.IsTrue(m1.Contains("ПРИЁМКИ"), "отказ про ячейку приёмки, факт: {0}", m1);

        var m2 = await RejectedAsync(() => PutAwayAsync(y.Receiving, y.Picking, y.Item, 1m),
            "раскладка кладёт не на хранение");
        Assert.IsTrue(m2.Contains("ХРАНЕНИЯ"), "отказ про ячейку хранения, факт: {0}", m2);

        var m3 = await RejectedAsync(() => PickAsync(y.Receiving, y.Picking, y.Item, 1m),
            "отбор забирает не с хранения");
        Assert.IsTrue(m3.Contains("ХРАНЕНИЯ"), "отказ про ячейку хранения, факт: {0}", m3);

        var m4 = await RejectedAsync(() => PickAsync(y.Storage, y.Receiving, y.Item, 1m),
            "отбор кладёт не в отбор");
        Assert.IsTrue(m4.Contains("ОТБОРА"), "отказ про ячейку отбора, факт: {0}", m4);
    }

    [IntegrationTest("Дисциплина включена: приход не в приёмку и отгрузка не из отбора отклоняются")]
    public async Task WrongCellsRejectedForReceiptAndShipment()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);

        var m1 = await RejectedAsync(() => ReceiveAsync(y, y.Storage, 5m), "приход не в ячейку приёмки");
        Assert.IsTrue(m1.Contains("ПРИЁМКИ"), "отказ про ячейку приёмки, факт: {0}", m1);

        await SeedAsync(y.Storage, y.Item, 10m);
        var m2 = await RejectedAsync(() => SellAsync(y, y.Storage, 1m), "отгрузка не из ячейки отбора");
        Assert.IsTrue(m2.Contains("ОТБОРА"), "отказ про ячейку отбора, факт: {0}", m2);
    }

    [IntegrationTest("Нехватка в ячейке отклоняется и при ВЫКЛЮЧЕННОЙ дисциплине")]
    public async Task ShortageRejectedRegardlessOfDiscipline()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(false);

        await SeedAsync(y.Receiving, y.Item, 3m);

        // Флаг выключает ПОЛИТИКУ (роли ячеек), но не ФИЗИКУ: нельзя переставить
        // то, чего в ячейке нет. Регистр допускает отрицательный остаток, поэтому
        // движок здесь не защитит — защита обязана быть в событии.
        var msg = await RejectedAsync(() => PutAwayAsync(y.Receiving, y.Storage, y.Item, 5m),
            "раскладывается больше, чем лежит в ячейке");
        Assert.IsTrue(msg.Contains("Недостаточно"), "отказ про нехватку, факт: {0}", msg);

        Assert.IsTrue(await OnHandAsync(y.Receiving, y.Item) == 3m,
            "отклонённое задание склад не тронуло, факт {0}", await OnHandAsync(y.Receiving, y.Item));
    }

    [IntegrationTest("Дисциплина выключена: ячейки свободны, как раньше")]
    public async Task DisciplineOffKeepsCellsFree()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(false);

        // Приход в ячейку ХРАНЕНИЯ и отгрузка оттуда же — ровно так живут около
        // тридцати существующих тестов. Этот сценарий и есть их страховка.
        await ReceiveAsync(y, y.Storage, 8m);
        Assert.IsTrue(await OnHandAsync(y.Storage, y.Item) == 8m,
            "приход в ячейку хранения прошёл, факт {0}", await OnHandAsync(y.Storage, y.Item));

        await SellAsync(y, y.Storage, 3m);
        Assert.IsTrue(await OnHandAsync(y.Storage, y.Item) == 5m,
            "отгрузка из ячейки хранения прошла, факт {0}", await OnHandAsync(y.Storage, y.Item));

        // Задание тоже не привередничает: из хранения в отбор при выключенном флаге.
        await PutAwayAsync(y.Storage, y.Picking, y.Item, 5m);
        Assert.IsTrue(await OnHandAsync(y.Picking, y.Item) == 5m,
            "раскладка сработала между любыми ячейками, факт {0}", await OnHandAsync(y.Picking, y.Item));
    }

    [IntegrationTest("Перепроведение прихода не плодит второе задание раскладки")]
    public async Task RepostDoesNotDuplicatePutAwayTask()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);

        var order = await ReceiveAsync(y, y.Receiving, 6m);

        var task = await SinglePutAwayAsync(order.MetaId);
        Assert.IsTrue(task.Subtype == PutAwayTask.Subtypes.Draft,
            "задание создано черновиком — товар физически ещё не переставили, факт {0}", task.Subtype);
        Assert.IsTrue(task.FromCell == y.Receiving, "забирать из ячейки прихода");
        Assert.IsTrue(task.Lines.Count == 1 && task.Lines[0].ToCell == y.Storage,
            "строка ведёт в ячейку хранения");
        Assert.IsTrue(await OnHandAsync(y.Receiving, y.Item) == 6m,
            "черновик задания склад не двигает, факт {0}", await OnHandAsync(y.Receiving, y.Item));

        // ВОТ ЗДЕСЬ проверка обретает зубы. Событие after-post исполняется заново
        // при каждом проведении, и перепроведение прихода (правка накладной —
        // обычное дело) без защиты завело бы ВТОРОЕ задание на тот же товар.
        //
        // Партия себестоимости, которой удваиваются продажи, тут ни при чём:
        // приход увеличивает склад, драйвер CostingIssue срабатывает только на
        // чистом минусе и вторичных движений не пишет. Для прихода источник
        // повтора — именно перепроведение.
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);

        var again = await SinglePutAwayAsync(order.MetaId);
        Assert.IsTrue(again.MetaId == task.MetaId,
            "после перепроведения задание ТО ЖЕ, а не новое: было {0}, стало {1}", task.MetaId, again.MetaId);
    }

    /// <summary>Ровно одно задание раскладки в семье документа — и оно же
    /// возвращается. Само утверждение «ровно одно» и есть предмет проверки.</summary>
    private static async Task<PutAwayTask> SinglePutAwayAsync(Guid orderId)
    {
        var family = await DocumentManager.GetDocumentFamilyAsync(orderId);
        var tasks = family.Nodes.Where(n => n.DocTypeName == "PutAwayTask").ToList();
        Assert.IsTrue(tasks.Count == 1, "заданий раскладки ровно одно, факт {0}", tasks.Count);

        var task = await DocumentManager.GetDocumentAsync<PutAwayTask>(tasks[0].DocId);
        Assert.IsNotNull(task, "задание читается");
        return task!;
    }

    [IntegrationTest("Дисциплина выключена: приход заданий не плодит")]
    public async Task DisciplineOffSpawnsNoTasks()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(false);

        var order = await ReceiveAsync(y, y.Receiving, 4m);

        var family = await DocumentManager.GetDocumentFamilyAsync(order.MetaId);
        Assert.IsTrue(!family.Nodes.Any(n => n.DocTypeName == "PutAwayTask"),
            "при выключенной дисциплине приход не порождает заданий");
    }
}
