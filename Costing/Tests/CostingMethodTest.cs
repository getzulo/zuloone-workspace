using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (Item, PurchaseOrder, StockAdjustment, …Row). Тест-скриптам
// этот namespace НЕ приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// МЕТОД СЕБЕСТОИМОСТИ — НАСТРОЙКА, А НЕ КОНСТАНТА В КОДЕ.
//
// Выбытие себестоимости не пишет ни один транзакционный скрипт: его порождает
// драйвер итогов CostingIssue, висящий на регистре Stock, — «уменьшился
// складской остаток, списалась себестоимость». Чем оценивать выбывшее, решает
// второй драйвер, CostingValuation на регистре ItemCostFifo, и решает он это по
// записи справочника CostingSettings.
//
// Поэтому тест ведёт себя как пользователь: меняет НАСТРОЙКУ и проводит обычные
// документы — заказ поставщику и складское списание. Ни одной прямой проводки
// в регистр: ровно тот путь, которым цифры появляются в проме. Два прогона на
// одних и тех же данных дают РАЗНЫЕ остатки, и разница — это и есть метод:
//
//   два лота 10×7 = 70 и 10×9 = 90, списываем 15 штук
//     FIFO — старейшие первыми: 10×7 + 5×9 = 115, остаток 5 штук на 45
//     AVG  — средневзвешенная 160/20 = 8: 15×8 = 120, остаток 5 штук на 40
//
// 45 против 40 на одних и тех же документах — утверждение, которое невозможно
// пройти «мимо» настройки.
public class CostingMethodTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid CellA;
        public Guid CellB;
        public Guid Item;
        public Guid Supplier;
    }

    // ───────────────────────────── мастер-данные ─────────────────────────────

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
        legalEntity.RegistrationNumber = "REG-METHOD-1";
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

        // Справочники общие на весь стенд, рядом идут прогоны других агентов —
        // коды обязаны быть уникальными для этого прогона.
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

        return new Setup
        {
            CellA = await NewCellAsync(zone.MetaId, cellType.MetaId, "A-01", 1),
            CellB = await NewCellAsync(zone.MetaId, cellType.MetaId, "B-01", 2),
            Item = item.MetaId,
            Supplier = supplier.MetaId,
        };
    }

    private static async Task<Guid> NewCellAsync(Guid zone, Guid cellType, string name, int number)
    {
        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = name;
        cell.Type = cellType;
        cell.StoreZone = zone;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = number;
        return (await DictionaryManager.SaveRecordAsync(cell)).MetaId;
    }

    /// <summary>Настройка модуля — singleton-справочник: правим запись, если она
    /// есть на стенде, иначе заводим. Прогон откатывается, так что стенду это
    /// ничего не оставляет.</summary>
    private static async Task SetMethodAsync(string method, bool roundCosts = false)
    {
        var rows = await DictionaryManager.GetRecordsAsync<CostingSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<CostingSettings>();
        settings.CostingMethod = method;
        settings.RoundCosts = roundCosts;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    // ───────────────────────────── чтение регистров ──────────────────────────

    private static Task<decimal> FifoAsync(string resource, Guid item)
        => TotalsManager.GetBalanceAsync("ItemCostFifo", resource,
            new Dictionary<string, object?> { ["Item"] = item });

    private static Task<decimal> StockAsync(Guid cell, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item });

    /// <summary>Сумма ресурса по ВСЕМУ регистру: разрез InventoryValue несёт
    /// ДИНАМИЧЕСКАЯ аналитика Item, физических измерений у него нет и точечный
    /// срез не адресуется. Поэтому утверждения по нему — на ПРИРАЩЕНИИ к снимку,
    /// снятому до шага: соседние прогоны на общем стенде так не мешают.</summary>
    private static async Task<decimal> InventoryValueAsync(string resource)
    {
        decimal sum = 0m;
        foreach (var row in await TotalsManager.QueryBalancesAsync("InventoryValue"))
            if (row.TryGetValue(resource, out var v) && v != null) sum += Convert.ToDecimal(v);
        return sum;
    }

    // ─────────────────────────────── документы ───────────────────────────────

    /// <summary>Приход: заказ поставщику объявленным маршрутом Draft → Ordered →
    /// Received. Слои себестоимости создаёт именно оприходование.</summary>
    private static async Task ReceiveAsync(Setup s, decimal qty, decimal price)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.CellA;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
    }

    /// <summary>Расход: обычное складское списание. Никакой ноги себестоимости в
    /// его проводках нет — она появится сама, потому что уменьшился остаток.</summary>
    private static async Task WriteOffAsync(Setup s, decimal qty)
    {
        var doc = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        doc.Cell = s.CellA;
        doc.Reason = "Бой при выкладке";
        doc.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = -qty });
        await DocumentManager.SaveDocumentAsync(doc);

        doc.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);
    }

    // ─────────────────────────────── сценарии ────────────────────────────────

    [IntegrationTest("Себестоимость: FIFO списывает старейшие лоты по документу")]
    public async Task FifoIssueConsumesOldestLots()
    {
        var s = await SetupAsync();
        // Метод задаём ЯВНО, а не полагаемся на умолчание: иначе тест проверял бы
        // «что настроено на стенде», а не «что делает FIFO».
        await SetMethodAsync("FIFO");

        var value0 = await InventoryValueAsync("Value");
        var qty0 = await InventoryValueAsync("Qty");

        await ReceiveAsync(s, 10m, 7m);   // лот 1: 10 шт по 7 = 70
        await ReceiveAsync(s, 10m, 9m);   // лот 2: 10 шт по 9 = 90

        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 20m,
            "после двух приходов 20 штук в слоях, факт {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 160m,
            "после двух приходов 160 денег в слоях, факт {0}", await FifoAsync("Amount", s.Item));
        Assert.IsTrue(await StockAsync(s.CellA, s.Item) == 20m, "на складе 20 штук");

        await WriteOffAsync(s, 15m);

        Assert.IsTrue(await StockAsync(s.CellA, s.Item) == 5m,
            "на складе 20 − 15 = 5 штук, факт {0}", await StockAsync(s.CellA, s.Item));

        var fifoQty = await FifoAsync("Quantity", s.Item);
        var fifoAmount = await FifoAsync("Amount", s.Item);
        Assert.IsTrue(fifoQty == 5m, "в слоях осталось 5 штук, факт {0}", fifoQty);
        // 45 — это ровно «остался второй лот»: 5 × 9. Средняя дала бы 40, и
        // разница между числами и есть проверяемый метод.
        Assert.IsTrue(fifoAmount == 45m,
            "FIFO гасит старейший лот первым: остаток 5 × 9 = 45, факт {0} (средняя дала бы 40)", fifoAmount);

        // Стоимость запаса обязана двигаться той же величиной: списано 115.
        var value = await InventoryValueAsync("Value") - value0;
        var qty = await InventoryValueAsync("Qty") - qty0;
        Assert.IsTrue(qty == 5m, "количество запаса +5, факт {0}", qty);
        Assert.IsTrue(value == 45m, "стоимость запаса 160 − 115 = 45, факт {0}", value);
    }

    [IntegrationTest("Себестоимость: AVG оценивает выбытие по средневзвешенной")]
    public async Task AverageIssueUsesWeightedAverage()
    {
        var s = await SetupAsync();
        // RoundCosts заодно: округление себестоимости — второй флаг настройки, и
        // он обязан пройти тот же путь. Проверить его ЧИСЛОМ на этих данных
        // нельзя — ресурс Amount и так хранится с двумя знаками (EDT Money),
        // поэтому здесь он покрыт как исполняемый путь, а не как значение.
        await SetMethodAsync("AVG", roundCosts: true);

        var value0 = await InventoryValueAsync("Value");
        var qty0 = await InventoryValueAsync("Qty");

        await ReceiveAsync(s, 10m, 7m);   // те же два лота, что и в FIFO-прогоне
        await ReceiveAsync(s, 10m, 9m);

        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 160m,
            "приходы одинаковы в обоих методах: 160, факт {0}", await FifoAsync("Amount", s.Item));

        await WriteOffAsync(s, 15m);

        var fifoQty = await FifoAsync("Quantity", s.Item);
        var fifoAmount = await FifoAsync("Amount", s.Item);
        Assert.IsTrue(fifoQty == 5m, "в слоях осталось 5 штук, факт {0}", fifoQty);
        // 160/20 = 8 за штуку; 15 × 8 = 120 списано, 40 осталось. Ровно те же
        // документы, что и в FIFO-прогоне, дают ДРУГОЕ число — значит настройка
        // действительно решает.
        Assert.IsTrue(fifoAmount == 40m,
            "AVG оценивает выбытие по 160/20 = 8: остаток 160 − 120 = 40, факт {0} (FIFO дал бы 45)", fifoAmount);

        var value = await InventoryValueAsync("Value") - value0;
        var qty = await InventoryValueAsync("Qty") - qty0;
        Assert.IsTrue(qty == 5m, "количество запаса +5, факт {0}", qty);
        Assert.IsTrue(value == 40m, "стоимость запаса по средней = 40, факт {0}", value);

        // Средняя гасит лоты ДОЛЯМИ — оба лота остались открытыми на 2.5 штуки.
        // Дробное состояние обязано быть рабочим, а не «почти правильным»:
        // добираем остаток и требуем чистый ноль. Зависни в лотах хоть копейка
        // неснимаемой стоимости — она осталась бы в оценке запаса навсегда.
        await WriteOffAsync(s, 5m);
        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 0m,
            "остаток выбран до нуля, факт {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 0m,
            "вся стоимость списана, в слоях ноль, факт {0}", await FifoAsync("Amount", s.Item));
        Assert.IsTrue(await InventoryValueAsync("Value") - value0 == 0m,
            "стоимость запаса вернулась к исходной, факт {0}", await InventoryValueAsync("Value") - value0);
    }

    [IntegrationTest("Себестоимость: перемещение между ячейками не является выбытием")]
    public async Task TransferIsNotConsumption()
    {
        var s = await SetupAsync();
        await SetMethodAsync("FIFO");

        var value0 = await InventoryValueAsync("Value");
        var qty0 = await InventoryValueAsync("Qty");

        await ReceiveAsync(s, 10m, 7m);

        var transfer = await DocumentManager.NewDocumentAsync<StockTransfer>();
        transfer.FromCell = s.CellA;
        transfer.ToCell = s.CellB;
        transfer.Lines.Add(new StockTransferLinesTablePartRow { Item = s.Item, Quantity = 4m });
        await DocumentManager.SaveDocumentAsync(transfer);

        Assert.IsTrue(await StockAsync(s.CellB, s.Item) == 0m, "черновик перемещения склад не двигает");

        transfer.Subtype = StockTransfer.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(transfer);

        // Товар переехал — и это ДВА движения склада, одно из них отрицательное.
        Assert.IsTrue(await StockAsync(s.CellA, s.Item) == 6m,
            "из A ушло 4: осталось 6, факт {0}", await StockAsync(s.CellA, s.Item));
        Assert.IsTrue(await StockAsync(s.CellB, s.Item) == 4m,
            "в B приехало 4, факт {0}", await StockAsync(s.CellB, s.Item));

        // Но предприятие от переноса коробки с полки на полку не обеднело:
        // драйвер схлопывает движения документа по товару и видит чистый ноль.
        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 10m,
            "слои FIFO перемещением не тронуты: 10 штук, факт {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 70m,
            "сумма слоёв перемещением не тронута: 70, факт {0}", await FifoAsync("Amount", s.Item));

        var value = await InventoryValueAsync("Value") - value0;
        var qty = await InventoryValueAsync("Qty") - qty0;
        Assert.IsTrue(qty == 10m, "количество запаса всё те же +10, факт {0}", qty);
        Assert.IsTrue(value == 70m, "стоимость запаса всё те же +70, факт {0}", value);
    }

    [IntegrationTest("Себестоимость: отмена проведения возвращает списанное")]
    public async Task UnpostRestoresCost()
    {
        var s = await SetupAsync();
        await SetMethodAsync("FIFO");

        var value0 = await InventoryValueAsync("Value");

        await ReceiveAsync(s, 10m, 7m);

        // Списание заводим здесь, а не через хелпер: его нужно потом вернуть в
        // черновик, значит документ должен остаться в руках.
        var doc = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        doc.Cell = s.CellA;
        doc.Reason = "Бой при выкладке";
        doc.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = -4m });
        await DocumentManager.SaveDocumentAsync(doc);

        doc.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);

        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 6m,
            "после списания в слоях 6 штук, факт {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 42m,
            "после списания в слоях 6 × 7 = 42, факт {0}", await FifoAsync("Amount", s.Item));

        // Возврат в черновик разносит движения документа обратно. Расходная нога
        // себестоимости — тоже ЕГО движения (драйвер пишет их с тем же docId), и
        // отмена обязана снять их вместе со складскими. Иначе оценка запаса
        // навсегда теряла бы столько, сколько успел списать отменённый документ.
        doc.Subtype = StockAdjustment.Subtypes.Draft;
        await DocumentManager.SaveDocumentAsync(doc);

        Assert.IsTrue(await StockAsync(s.CellA, s.Item) == 10m,
            "отмена вернула склад к 10 штукам, факт {0}", await StockAsync(s.CellA, s.Item));
        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 10m,
            "отмена вернула слои к 10 штукам, факт {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 70m,
            "отмена вернула сумму слоёв к 70, факт {0}", await FifoAsync("Amount", s.Item));
        Assert.IsTrue(await InventoryValueAsync("Value") - value0 == 70m,
            "отмена вернула стоимость запаса к +70, факт {0}", await InventoryValueAsync("Value") - value0);
    }
}
