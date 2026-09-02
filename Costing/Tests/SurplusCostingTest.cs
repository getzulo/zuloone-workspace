using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (Item, StockAdjustment, StockCount, …Row). Тест-скриптам
// этот namespace НЕ приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// БЕЗВОЗМЕЗДНЫЙ ПРИХОД ОБЯЗАН ПОЛУЧИТЬ СЕБЕСТОИМОСТЬ.
//
// Драйвер CostingIssue списывает себестоимость выбывшего, но партии заводит не
// он: их создаёт оприходование заказа поставщику и выпуск производства. Излишек
// корректировки и пересчёт вверх при инвентаризации не заводили партию НИКТО —
// товар появлялся на складе без себестоимости и потом молча списывался по нулю,
// потому что драйвер берёт Math.Min(выбывшее, наличное в партиях), а наличного
// не было.
//
// Проверяется именно СКВОЗНОЕ последствие, а не факт записи в регистр: находим
// излишек, затем списываем его — и стоимость запаса обязана уменьшиться. Пока
// партии не было, второй шаг не двигал стоимость вовсе.
//
// Все документы проводятся по-настоящему; прямых движений в ItemCostFifo и
// InventoryValue нет ни одного.
public class SurplusCostingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Cell;
        public Guid Item;
        public Guid Supplier;
    }

    private async Task<Setup> SetupAsync()
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
        legalEntity.RegistrationNumber = $"REG-SURP-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "WH";
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

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"STG-{Db.NewId():N}"[..12];
        cellType.Name = "Storage";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PCS-{Db.NewId():N}"[..12];
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"MERCH-{Db.NewId():N}"[..12];
        group.Name = "Merchandise";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item = await DictionaryManager.SaveRecordAsync(item);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "A-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        // Метод задаём явно: иначе тест проверял бы «что настроено на стенде».
        var rows = await DictionaryManager.GetRecordsAsync<CostingSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<CostingSettings>();
        settings.CostingMethod = "FIFO";
        settings.RoundCosts = false;
        await DictionaryManager.SaveRecordAsync(settings);

        return new Setup { Cell = cell.MetaId, Item = item.MetaId, Supplier = supplier.MetaId };
    }

    private static async Task ReceiveAsync(Setup s, decimal qty, decimal price)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Cell;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
    }

    /// <summary>Корректировка остатка: плюс — излишек, минус — недостача/бой.</summary>
    private static async Task AdjustAsync(Setup s, decimal qty, string reason)
    {
        var doc = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        doc.Cell = s.Cell;
        doc.Reason = reason;
        doc.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = qty });
        await DocumentManager.SaveDocumentAsync(doc);

        doc.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);
    }

    private static Task<decimal> FifoAsync(string resource, Guid item)
        => TotalsManager.GetBalanceAsync("ItemCostFifo", resource,
            new Dictionary<string, object?> { ["Item"] = item });

    /// <summary>Стоимость запаса суммой: у InventoryValue разрез несёт
    /// динамическая аналитика, точечный срез не адресуется — утверждения на
    /// ПРИРАЩЕНИИ к снимку, чтобы соседние прогоны не мешали.</summary>
    private static async Task<decimal> InventoryValueAsync()
    {
        decimal sum = 0m;
        foreach (var row in await TotalsManager.QueryBalancesAsync("InventoryValue"))
            if (row.TryGetValue("Value", out var v) && v != null) sum += Convert.ToDecimal(v);
        return sum;
    }

    [IntegrationTest("Излишек получает себестоимость по текущей средней")]
    public async Task SurplusIsValuedAtCurrentAverage()
    {
        var s = await SetupAsync();

        // 10 штук по 7 = 70. Средняя — 7 за штуку.
        await ReceiveAsync(s, 10m, 7m);
        var value0 = await InventoryValueAsync();

        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 10m, "в партиях 10 штук");
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 70m, "в партиях на 70");

        // Нашли ещё 5 штук. Покупной цены у них нет — оцениваем по текущей
        // средней: 5 × 7 = 35.
        await AdjustAsync(s, 5m, "Излишек при пересчёте");

        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 15m,
            "в партиях стало 15 штук, факт {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 105m,
            "70 + 5 × 7 = 105, факт {0}", await FifoAsync("Amount", s.Item));

        var value1 = await InventoryValueAsync();
        Assert.IsTrue(value1 - value0 == 35m,
            "стоимость запаса выросла на 35, факт {0}", value1 - value0);
    }

    [IntegrationTest("Списание найденного излишка уходит не по нулю")]
    public async Task WriteOffOfSurplusCarriesCost()
    {
        var s = await SetupAsync();

        await ReceiveAsync(s, 10m, 7m);
        await AdjustAsync(s, 5m, "Излишек при пересчёте");

        var value0 = await InventoryValueAsync();

        // Списываем ровно то количество, которое нашли. Пока партии на излишек не
        // заводилось, наличного в партиях хватало только на купленные 10, и это
        // списание уменьшало стоимость на 0 — товар уходил бесплатно.
        await AdjustAsync(s, -5m, "Бой");

        var value1 = await InventoryValueAsync();
        Assert.IsTrue(value0 - value1 == 35m,
            "списание 5 штук по 7 уменьшает стоимость на 35, факт {0}", value0 - value1);

        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 10m,
            "в партиях осталось 10 штук, факт {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 70m,
            "в партиях осталось на 70, факт {0}", await FifoAsync("Amount", s.Item));
    }

    [IntegrationTest("Излишек товара без партий заводится нулевым, а не выдумывает цену")]
    public async Task SurplusWithoutHistoryIsZeroCost()
    {
        var s = await SetupAsync();

        // Товар никогда не покупали: в системе нет ни одного факта о его цене.
        var value0 = await InventoryValueAsync();
        await AdjustAsync(s, 5m, "Излишек без истории");

        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 5m,
            "партия заведена на 5 штук, факт {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 0m,
            "цены взяться неоткуда — партия нулевая, факт {0}", await FifoAsync("Amount", s.Item));

        var value1 = await InventoryValueAsync();
        Assert.IsTrue(value1 - value0 == 0m,
            "стоимость запаса не выросла, факт {0}", value1 - value0);
    }

    [IntegrationTest("Инвентаризация вверх тоже заводит партию")]
    public async Task StockCountUpCreatesLayer()
    {
        var s = await SetupAsync();

        await ReceiveAsync(s, 10m, 7m);
        var value0 = await InventoryValueAsync();

        // Пересчёт показал 13 вместо 10: движения пишет OnBeforePost самого
        // документа, а не транзакционный скрипт — сервис считает нетто по ним и
        // потому одинаково работает с обоими способами.
        var count = await DocumentManager.NewDocumentAsync<StockCount>();
        count.Cell = s.Cell;
        count.CountDate = DateTime.UtcNow.Date;
        count.Lines.Add(new StockCountLinesTablePartRow { Item = s.Item, CountedQty = 13m });
        await DocumentManager.SaveDocumentAsync(count);

        count.Subtype = StockCount.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(count);

        // Сначала — что инвентаризация вообще двигает склад. До исправления она
        // писала движения в OnBeforePost, и уборка движка стирала их сразу же:
        // документ не менял остаток ни на единицу, и заметить это было негде —
        // тестов у него не было вовсе.
        var stock = await TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Item"] = s.Item, ["Cell"] = s.Cell });
        Assert.IsTrue(stock == 13m, "пересчёт довёл остаток до 13, факт {0}", stock);

        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 13m,
            "в партиях стало 13 штук, факт {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 91m,
            "70 + 3 × 7 = 91, факт {0}", await FifoAsync("Amount", s.Item));

        var value1 = await InventoryValueAsync();
        Assert.IsTrue(value1 - value0 == 21m,
            "стоимость запаса выросла на 3 × 7 = 21, факт {0}", value1 - value0);
    }

    [IntegrationTest("Черновик инвентаризации склад не двигает")]
    public async Task StockCountDraftDoesNotMoveStock()
    {
        // РЕГРЕССИЯ, ВОЗМОЖНАЯ ИМЕННО ПОСЛЕ ПЕРЕВОДА ДОКУМЕНТА НА postOnSave.
        // С postOnSave: true цикл проведения запускается на КАЖДОМ сохранении, в
        // том числе черновика. Обработчик, пишущий движения без проверки подтипа,
        // подвинет склад ещё до проведения: кладовщик вбил факт, нажал «Сохранить»,
        // передумал — а остаток уже испорчен, и снять движения нечем, потому что
        // подтип не менялся.
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);

        var count = await DocumentManager.NewDocumentAsync<StockCount>();
        count.Cell = s.Cell;
        count.CountDate = DateTime.UtcNow.Date;
        count.Lines.Add(new StockCountLinesTablePartRow { Item = s.Item, CountedQty = 13m });
        await DocumentManager.SaveDocumentAsync(count);

        var stock = await TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Item"] = s.Item, ["Cell"] = s.Cell });
        Assert.IsTrue(stock == 10m,
            "черновик пересчёта не должен двигать склад: остаётся 10, факт {0}", stock);
    }

    [IntegrationTest("Пересчёт в неосновной единице не создаёт фиктивной недостачи")]
    public async Task StockCountInNonBaseUnitUsesBaseQuantity()
    {
        // САМАЯ ДОРОГАЯ ИЗ ДЫР ЭТОГО КЛАССА, И ДО СИХ ПОР НЕ ПОКРЫТАЯ.
        //
        // Инвентаризация считает дельту как «факт минус остаток», а остаток лежит
        // в БАЗОВОЙ единице. Пересчитай кладовщик товар в ящиках и возьми код
        // сырое CountedQty — «1 ящик» встретится с «10 штук» на полке и даст
        // дельту −9: система запишет недостачу девяти штук, которых никто не терял.
        // Обратный случай ещё хуже: фиктивный ИЗЛИШЕК тут же капитализируется
        // сервисом себестоимости в ItemCostFifo и InventoryValue, то есть выдуманный
        // товар получает выдуманную стоимость и попадает в оценку запаса.
        //
        // Здесь 1 ящик = 12 штук при остатке 10, поэтому верный ответ — излишек в
        // 2 штуки. По сырому количеству вышла бы недостача в 9.
        const decimal boxFactor = 12m;

        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);        // 10 штук по 7 = 70, средняя 7

        var box = DictionaryManager.NewRecord<UnitOfMeasure>();
        box.Name = "Box";
        box.Code = $"BOX-{Db.NewId():N}"[..12];
        box.DecimalPlaces = 0;
        box = await DictionaryManager.SaveRecordAsync(box);

        var pack = DictionaryManager.NewRecord<ItemUnit>();
        pack.Item = s.Item;
        pack.Unit = box.MetaId;
        pack.QtyInBaseUnit = boxFactor;
        await DictionaryManager.SaveRecordAsync(pack);

        var value0 = await InventoryValueAsync();

        var count = await DocumentManager.NewDocumentAsync<StockCount>();
        count.Cell = s.Cell;
        count.CountDate = DateTime.UtcNow.Date;
        count.Lines.Add(new StockCountLinesTablePartRow
        {
            Item = s.Item,
            CountedQty = 1m,
            Unit = box.MetaId,
        });
        await DocumentManager.SaveDocumentAsync(count);

        // Нормализатор обязан был пересчитать 1 ящик в 12 штук на записи строки —
        // без этого проверка ниже проверяла бы совсем не то, что заявлено.
        var stored = (await DocumentManager.GetDocumentAsync<StockCount>(count.MetaId))!;
        Assert.IsTrue(stored.Lines[0].BaseQuantity == boxFactor,
            "1 ящик по {0} = {0} штук, факт {1}", boxFactor, stored.Lines[0].BaseQuantity);

        count.Subtype = StockCount.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(count);

        var stock = await TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Item"] = s.Item, ["Cell"] = s.Cell });
        Assert.IsTrue(stock == boxFactor,
            "пересчёт довёл остаток до 12 штук (по сырому вышла бы недостача до 1), факт {0}", stock);

        // Излишек в 2 штуки капитализирован по текущей средней 7 → +14.
        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == boxFactor,
            "в партиях 12 штук, факт {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 84m,
            "70 + 2 × 7 = 84, факт {0}", await FifoAsync("Amount", s.Item));

        var value1 = await InventoryValueAsync();
        Assert.IsTrue(value1 - value0 == 14m,
            "стоимость запаса выросла ровно на найденные 2 × 7 = 14, факт {0}", value1 - value0);
    }

    [IntegrationTest("Партия излишка инвентаризации датируется CountDate, не сегодня")]
    public async Task StockCountSurplusLayerUsesCountDate()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);

        var countDate = DateTime.UtcNow.Date.AddDays(-10);
        var count = await DocumentManager.NewDocumentAsync<StockCount>();
        count.Cell = s.Cell;
        count.CountDate = countDate;
        count.Lines.Add(new StockCountLinesTablePartRow { Item = s.Item, CountedQty = 13m });
        await DocumentManager.SaveDocumentAsync(count);

        count.Subtype = StockCount.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(count);

        var dated = 0;
        foreach (var row in await TotalsManager.QueryMovementsAsync(
            "ItemCostFifo", $"[DocumentMetaId] = '{count.MetaId}'"))
        {
            if (row["Amount"] is null || Convert.ToDecimal(row["Amount"]) <= 0m) continue;
            var movementDate = Convert.ToDateTime(row["MovementDate"]).Date;
            Assert.IsTrue(movementDate == countDate,
                "слой излишка обязан лечь на CountDate {0}, факт {1}", countDate, movementDate);
            dated++;
        }
        Assert.IsTrue(dated > 0, "инвентаризация вверх обязана завести партию");
    }

    [IntegrationTest("Партия излишка корректировки датируется DocumentDate, не сегодня")]
    public async Task StockAdjustmentSurplusLayerUsesDocumentDate()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);

        var docDate = DateTime.UtcNow.Date.AddDays(-7);
        var doc = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        doc.Cell = s.Cell;
        doc.DocumentDate = docDate;
        doc.Reason = "Found";
        doc.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = 3m });
        await DocumentManager.SaveDocumentAsync(doc);

        doc.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);

        var dated = 0;
        foreach (var row in await TotalsManager.QueryMovementsAsync(
            "ItemCostFifo", $"[DocumentMetaId] = '{doc.MetaId}'"))
        {
            if (row["Amount"] is null || Convert.ToDecimal(row["Amount"]) <= 0m) continue;
            var movementDate = Convert.ToDateTime(row["MovementDate"]).Date;
            Assert.IsTrue(movementDate == docDate,
                "слой излишка обязан лечь на DocumentDate {0}, факт {1}", docDate, movementDate);
            dated++;
        }
        Assert.IsTrue(dated > 0, "корректировка вверх обязана завести партию");
    }
}
