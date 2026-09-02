using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Отчёт по регистру — платформенный сервис, не менеджер: он строит период
// (вход/приход/расход/остаток) с группировкой по ВЛОЖЕННЫМ путям измерений.
// Это ровно тот экран, который видит пользователь, поэтому цифры сверяются с ним,
// а не только с балансовой таблицей.
using ZuloOne.Core.Services;
// Генерённые классы (Item, PurchaseOrder, StockTransfer, SalesInvoice, …Row).
// Тест-скриптам этот namespace НЕ приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// ПОЛНЫЙ ТОРГОВЫЙ ЦИКЛ В НЕБАЗОВЫХ ЕДИНИЦАХ: закупка → перемещение между
// складами → списание → продажа, с проверкой КАЖДОГО регистра после каждого шага
// и сверкой отчёта по остаткам в разрезе СКЛАДОВ.
//
// Почему здесь. GLIntegration — единственная модель, чьи объявленные зависимости
// накрывают и Purchasing, и Sales, и Inventory сразу; цикл, который начинается
// заказом поставщику и кончается счётом покупателю, больше поставить некуда, не
// нарушив слои.
//
// Товар живёт в ШТУКАХ, а торгуется в ЯЩИКАХ (1 ящик = 12 штук). Это и есть суть
// проверки: остаток обязан копиться в базовой единице (иначе 2 ящика и 24 штуки
// сложились бы в 26), а деньги — считаться по ВВЕДЁННОЙ (в счёте продано 5
// ящиков по цене за ящик). Обе конвенции живут в одной строке документа, и
// разъезжаются они молча.
//
// Арифметика цикла, вся в штуках:
//   закупка   5 ящиков  = +60 на MAIN                          MAIN 60  SHOP  0
//   перемещение 2 ящика =  −24 MAIN / +24 SHOP                  MAIN 36  SHOP 24
//   списание  3 штуки   =   −3 SHOP                             MAIN 36  SHOP 21
//   продажа   1 ящик    =  −12 MAIN                             MAIN 24  SHOP 21
public class UnitAwareTradeCycleTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private const decimal BoxFactor = 12m;     // 1 ящик = 12 штук
    private const decimal PurchasePricePerBox = 120m;
    private const decimal SalePricePerBox = 300m;

    private sealed class Setup
    {
        public Guid MainCell;      // ячейка склада «Центральный»
        public Guid ShopCell;      // ячейка склада «Магазин»
        public Guid MainStore;
        public Guid ShopStore;
        public Guid Item;
        public Guid Piece;         // базовая единица товара
        public Guid Box;           // единица ввода
        public Guid Supplier;
        public Guid Customer;
    }

    // ───────────────────────────── мастер-данные ─────────────────────────────

    private async Task<Guid> NewUnitAsync(string name, string code, int decimals)
    {
        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = name;
        // Справочник общий на весь стенд, а рядом идут прогоны других агентов —
        // код обязан быть уникальным для этого прогона.
        unit.Code = $"{code}-{Db.NewId():N}"[..12];
        unit.DecimalPlaces = decimals;
        return (await DictionaryManager.SaveRecordAsync(unit)).MetaId;
    }

    /// <summary>Склад = Store с одной зоной и одной ячейкой. Регистр остатков
    /// физически ведётся по ЯЧЕЙКЕ, поэтому «остаток склада» — это остаток его
    /// ячейки, а разрез по складу отчёт получает переходом Cell → Zone → Store.</summary>
    private async Task<(Guid Store, Guid Cell)> NewWarehouseAsync(string name, Guid division, Guid cellType, int cellNumber)
    {
        var store = DictionaryManager.NewRecord<Store>();
        store.Name = name;
        store.Division = division;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = $"{name} — зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = $"{name}-01";
        cell.Type = cellType;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = cellNumber;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        return (store.MetaId, cell.MetaId);
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
        legalEntity.RegistrationNumber = "REG-CYCLE-1";
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

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"STG-{Db.NewId():N}"[..12];
        cellType.Name = "Storage";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var main = await NewWarehouseAsync("MAIN", division.MetaId, cellType.MetaId, cellNumber: 1);
        var shop = await NewWarehouseAsync("SHOP", division.MetaId, cellType.MetaId, cellNumber: 2);

        // Единицы. Сколько штук в ящике — свойство ТОВАРА, поэтому упаковка
        // заводится ниже, после него. Создаётся она внутри отката раннера
        // намеренно: платформа читает её через соединение, которое уже держит,
        // внутри транзакции вызывающего — конвертер со своим соединением этих
        // строк не увидел бы вовсе.
        var piece = await NewUnitAsync("Piece", "PCS", 0);
        var box = await NewUnitAsync("Box", "BOX", 0);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"MERCH-{Db.NewId():N}"[..12];
        group.Name = "Merchandise";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bottled water";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = piece;      // базовая единица — цель пересчёта
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        // Упаковка этого товара: 1 ящик = BoxFactor штук.
        var pack = DictionaryManager.NewRecord<ItemUnit>();
        pack.Item = item.MetaId;
        pack.Unit = box;
        pack.QtyInBaseUnit = BoxFactor;
        await DictionaryManager.SaveRecordAsync(pack);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Water Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Corner Shop";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        // НАЛОГОВЫЙ КОНТУР. Нужен потому, что страновой НДС КСА больше не берёт
        // ставку из плоской константы: выставление счёта фиксирует на документе
        // ставку, подобранную по налоговому коду и ДАТЕ счёта. Без контура ставки
        // нет, и НДС не начисляется — ровно как раньше без заведённой константы.
        await TaxCircuitAsync();

        return new Setup
        {
            MainCell = main.Cell, ShopCell = shop.Cell,
            MainStore = main.Store, ShopStore = shop.Store,
            Item = item.MetaId, Piece = piece, Box = box,
            Supplier = supplier.MetaId, Customer = customer.MetaId,
        };
    }

    /// <summary>Налог → ставка 15% → код → настройки: минимальный контур, из
    /// которого выставление счёта берёт действующую на дату счёта ставку.</summary>
    private async Task TaxCircuitAsync()
    {
        var from = new DateTime(2020, 1, 1);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"AU-{Db.NewId():N}"[..10];
        authority.Name = "ZATCA";
        authority.CountryCode = "SA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"JU-{Db.NewId():N}"[..10];
        jurisdiction.Name = "Saudi Arabia";
        jurisdiction.CountryCode = "SA";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"VT-{Db.NewId():N}"[..10];
        tax.Name = "Saudi VAT";
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = from;
        tax = await DictionaryManager.SaveRecordAsync(tax);

        var rate = DictionaryManager.NewRecord<TaxRate>();
        rate.Tax = tax.MetaId;
        rate.Code = $"R-{Db.NewId():N}"[..10];
        rate.Rate = 0.15m;
        rate.EffectiveFrom = from;
        rate = await DictionaryManager.SaveRecordAsync(rate);

        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = $"STD-{Db.NewId():N}"[..10];
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"OUT-{Db.NewId():N}"[..10];
        code.Name = "Standard 15%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        code = await DictionaryManager.SaveRecordAsync(code);

        var direction = DictionaryManager.NewRecord<TaxDirection>();
        direction.Code = "OUTPUT";
        direction.Name = "Output";
        await DictionaryManager.SaveRecordAsync(direction);

        // Настройки налога — ОДИНОЧНЫЙ и КЭШИРУЕМЫЙ справочник: кэш переживает
        // откат кейса, поэтому правим существующую запись, а не заводим слепо.
        var rows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    // ───────────────────────────── чтение регистров ──────────────────────────

    /// <summary>Остаток ячейки по товару: у Stock ровно два физических измерения,
    /// так что срез задаётся полным ключом и чужие прогоны в него не попадают.</summary>
    private static Task<decimal> StockAsync(Guid cell, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item });

    /// <summary>ItemCostFifo адресуется измерением Item — тоже точный срез.</summary>
    private static Task<decimal> FifoAsync(string resource, Guid item)
        => TotalsManager.GetBalanceAsync("ItemCostFifo", resource,
            new Dictionary<string, object?> { ["Item"] = item });

    /// <summary>Сумма ресурса по ВСЕМУ регистру. Так читаются регистры, разрез
    /// которых несут ДИНАМИЧЕСКИЕ аналитики (Payable, Receivable, Revenue,
    /// VatPayable, InventoryValue): физических измерений у них нет, точечный срез
    /// не адресуется. Поэтому все утверждения по ним — на ПРИРАЩЕНИИ к снимку,
    /// сделанному до шага: соседние прогоны на общем стенде так не мешают, и
    /// снимок заодно и есть обязательная проверка состояния «до».</summary>
    private static async Task<decimal> TotalAsync(string register, string resource)
    {
        decimal sum = 0m;
        foreach (var row in await TotalsManager.QueryBalancesAsync(register))
            if (row.TryGetValue(resource, out var v) && v != null) sum += Convert.ToDecimal(v);
        return sum;
    }

    // ─────────────────────────────── документы ───────────────────────────────

    /// <summary>Заказ поставщику на N ящиков. Подтип не передаём: NewDocumentAsync
    /// обязан взять НАЧАЛЬНЫЙ подтип типа (Draft) сам.</summary>
    private async Task<PurchaseOrder> NewOrderAsync(Setup s, decimal boxes)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.MainCell;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow
        {
            Item = s.Item, Quantity = boxes, Unit = s.Box, UnitPrice = PurchasePricePerBox,
        });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    /// <summary>Закупка идёт ОБЪЯВЛЕННЫМ маршрутом Draft → Ordered → Received;
    /// прыжок сразу в Received движок отклоняет.</summary>
    private static async Task ReceiveAsync(PurchaseOrder order)
    {
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
    }

    private async Task<StockTransfer> NewTransferAsync(Setup s, decimal boxes)
    {
        var doc = await DocumentManager.NewDocumentAsync<StockTransfer>();
        doc.FromCell = s.MainCell;
        doc.ToCell = s.ShopCell;
        doc.Lines.Add(new StockTransferLinesTablePartRow { Item = s.Item, Quantity = boxes, Unit = s.Box });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }

    /// <summary>Списание вводится в БАЗОВОЙ единице — намеренно: в одном цикле
    /// встречаются обе, и пересчёт «единица сама в себя» обязан быть тождеством,
    /// а не отказом за отсутствием правила PCS→PCS.</summary>
    private async Task<StockAdjustment> NewWriteOffAsync(Setup s, decimal pieces)
    {
        var doc = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        doc.Cell = s.ShopCell;
        doc.Reason = "Бой при выкладке";
        doc.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = -pieces, Unit = s.Piece });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }

    private async Task<SalesInvoice> NewInvoiceAsync(Setup s, decimal boxes)
    {
        var doc = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        doc.Customer = s.Customer;
        doc.Location = s.MainCell;
        doc.Lines.Add(new SalesInvoiceLinesTablePartRow
        {
            Item = s.Item, Quantity = boxes, Unit = s.Box, UnitPrice = SalePricePerBox,
        });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }

    // ──────────────────────────────── сценарий ───────────────────────────────

    [IntegrationTest("Цикл закупка → перемещение → списание → продажа в ящиках")]
    public async Task FullCycle()
    {
        var s = await SetupAsync();

        // ── СОСТОЯНИЕ ДО: ни один регистр ещё не двигался по этому товару, а
        // денежные снимки берутся сейчас, чтобы утверждать приращения.
        Assert.IsTrue(await StockAsync(s.MainCell, s.Item) == 0m, "MAIN пуст до начала");
        Assert.IsTrue(await StockAsync(s.ShopCell, s.Item) == 0m, "SHOP пуст до начала");
        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 0m, "слоёв FIFO по товару ещё нет");
        var payable0 = await TotalAsync("Payable", "Amount");
        var receivable0 = await TotalAsync("Receivable", "Amount");
        var revenue0 = await TotalAsync("Revenue", "Amount");
        var vat0 = await TotalAsync("VatPayable", "Amount");
        var invValue0 = await TotalAsync("InventoryValue", "Value");
        var invQty0 = await TotalAsync("InventoryValue", "Qty");

        // ── 1. ЗАКУПКА: 5 ящиков по 120 за ящик на склад MAIN ────────────────
        var order = await NewOrderAsync(s, boxes: 5m);

        var storedOrder = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsTrue(storedOrder!.Lines[0].Quantity == 5m,
            "введённое количество остаётся 5 ящиков, а стало {0}", storedOrder.Lines[0].Quantity);
        Assert.IsTrue(storedOrder.Lines[0].BaseQuantity == 60m,
            "5 ящиков × 12 = 60 штук в BaseQuantity, а не {0}", storedOrder.Lines[0].BaseQuantity);

        // Размещённый заказ ещё ничего не двигает — движения принадлежат Received.
        Assert.IsTrue(await StockAsync(s.MainCell, s.Item) == 0m, "черновик заказа склад не двигает");
        Assert.IsTrue(await TotalAsync("Payable", "Amount") == payable0, "черновик заказа кредиторку не признаёт");

        await ReceiveAsync(order);

        var mainAfterReceipt = await StockAsync(s.MainCell, s.Item);
        Assert.IsTrue(mainAfterReceipt == 60m,
            "приход обязан лечь ШТУКАМИ: 60 на MAIN, а не {0} (ящики в регистре остатков)", mainAfterReceipt);
        Assert.IsTrue(await StockAsync(s.ShopCell, s.Item) == 0m, "SHOP приходом не затронут");

        // Деньги — по ВВЕДЁННОЙ единице: 5 ящиков × 120 = 600, а не 60 × 120.
        var payable = await TotalAsync("Payable", "Amount") - payable0;
        Assert.IsTrue(payable == 600m,
            "кредиторка = 5 ящиков × 120 за ящик = 600, а не {0}", payable);

        // Себестоимость: Value по введённой единице, Qty — по базовой. Их частное
        // и есть цена за ШТУКУ (600 / 60 = 10), которой FIFO оценивает выбытие.
        var invValue = await TotalAsync("InventoryValue", "Value") - invValue0;
        var invQty = await TotalAsync("InventoryValue", "Qty") - invQty0;
        Assert.IsTrue(invValue == 600m, "стоимость запаса +600, а не {0}", invValue);
        Assert.IsTrue(invQty == 60m, "количество запаса +60 штук, а не {0}", invQty);
        Assert.IsTrue(invValue / invQty == 10m, "цена за штуку 600/60 = 10, а не {0}", invValue / invQty);

        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 60m, "слой FIFO = 60 штук");
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 600m, "сумма слоя FIFO = 600");

        // ── 2. ПЕРЕМЕЩЕНИЕ MAIN → SHOP: 2 ящика = 24 штуки ───────────────────
        var transfer = await NewTransferAsync(s, boxes: 2m);
        Assert.IsTrue(await StockAsync(s.MainCell, s.Item) == 60m, "до проведения весь остаток на MAIN");
        Assert.IsTrue(await StockAsync(s.ShopCell, s.Item) == 0m, "до проведения SHOP пуст");

        transfer.Subtype = StockTransfer.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(transfer);

        var mainAfterTransfer = await StockAsync(s.MainCell, s.Item);
        var shopAfterTransfer = await StockAsync(s.ShopCell, s.Item);
        Assert.IsTrue(mainAfterTransfer == 36m, "на MAIN осталось 60 − 24 = 36, а не {0}", mainAfterTransfer);
        Assert.IsTrue(shopAfterTransfer == 24m, "на SHOP приехало 24, а не {0}", shopAfterTransfer);
        // Обе ноги обязаны взять ОДНО значение, иначе перемещение создаёт товар.
        Assert.IsTrue(mainAfterTransfer + shopAfterTransfer == 60m,
            "перемещение не создаёт и не уничтожает товар: сумма обязана остаться 60, а стала {0}",
            mainAfterTransfer + shopAfterTransfer);

        // ПЕРЕМЕЩЕНИЕ — НЕ ВЫБЫТИЕ. Драйвер себестоимости слушает движения
        // склада, а их здесь ДВА, и одно из них отрицательное. Прими он каждый
        // минус за расход — оценка запаса падала бы при переносе коробки с полки
        // на полку, то есть предприятие беднело бы от собственной логистики.
        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 60m,
            "перемещение не трогает слои FIFO: обязано остаться 60, а стало {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 600m,
            "перемещение не трогает сумму слоёв: обязано остаться 600, а стало {0}", await FifoAsync("Amount", s.Item));
        Assert.IsTrue(await TotalAsync("InventoryValue", "Value") - invValue0 == 600m,
            "перемещение не меняет стоимость запаса: обязано остаться +600, а стало {0}",
            await TotalAsync("InventoryValue", "Value") - invValue0);

        // ── 3. СПИСАНИЕ 3 штук со склада SHOP ────────────────────────────────
        var writeOff = await NewWriteOffAsync(s, pieces: 3m);

        var storedWriteOff = await DocumentManager.GetDocumentAsync<StockAdjustment>(writeOff.MetaId);
        // Тождество «штука → штука» обязано пройти пересчёт и сохранить ЗНАК:
        // недостача обязана остаться недостачей.
        Assert.IsTrue(storedWriteOff!.Lines[0].BaseQuantity == -3m,
            "списание в базовой единице даёт BaseQuantity −3, а не {0}", storedWriteOff.Lines[0].BaseQuantity);
        Assert.IsTrue(await StockAsync(s.ShopCell, s.Item) == 24m, "черновик списания остаток не трогает");

        writeOff.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(writeOff);

        var shopAfterWriteOff = await StockAsync(s.ShopCell, s.Item);
        Assert.IsTrue(shopAfterWriteOff == 21m, "на SHOP осталось 24 − 3 = 21, а не {0}", shopAfterWriteOff);
        Assert.IsTrue(await StockAsync(s.MainCell, s.Item) == 36m, "списание на SHOP склад MAIN не трогает");

        // А списание — ВЫБЫТИЕ, и цена штуки здесь 10: слой худеет на 3 × 10.
        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 57m,
            "списание сняло 3 штуки со слоёв: 60 − 3 = 57, а не {0}", await FifoAsync("Quantity", s.Item));
        Assert.IsTrue(await FifoAsync("Amount", s.Item) == 570m,
            "списание сняло 3 × 10 = 30 денег со слоёв: 600 − 30 = 570, а не {0}", await FifoAsync("Amount", s.Item));

        // ── 4. ПРОДАЖА 1 ящика со склада MAIN ────────────────────────────────
        var invoice = await NewInvoiceAsync(s, boxes: 1m);
        Assert.IsTrue(await StockAsync(s.MainCell, s.Item) == 36m, "черновик счёта склад не двигает");
        Assert.IsTrue(await TotalAsync("Receivable", "Amount") == receivable0, "черновик счёта дебиторку не признаёт");
        Assert.IsTrue(await TotalAsync("Revenue", "Amount") == revenue0, "черновик счёта выручку не признаёт");

        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);

        var mainAfterSale = await StockAsync(s.MainCell, s.Item);
        Assert.IsTrue(mainAfterSale == 24m,
            "продажа 1 ящика снимает 12 ШТУК: 36 − 12 = 24, а не {0}", mainAfterSale);
        Assert.IsTrue(await StockAsync(s.ShopCell, s.Item) == 21m, "продажа с MAIN склад SHOP не трогает");

        // Деньги счёта — по введённой единице: 1 ящик × 300.
        var receivable = await TotalAsync("Receivable", "Amount") - receivable0;
        var revenue = await TotalAsync("Revenue", "Amount") - revenue0;
        Assert.IsTrue(receivable == 300m, "дебиторка = 1 ящик × 300 = 300, а не {0}", receivable);
        Assert.IsTrue(revenue == 300m, "выручка = 300, а не {0}", revenue);

        // НДС КСА от той же базы. Ставка читается С ДОКУМЕНТА, а не из глобальной
        // константы: страновой скрипт берёт её из поля TaxRateApplied, куда
        // выставление счёта записывает ставку, действовавшую на дату документа.
        // Раньше тест сверялся с ТЕМ ЖЕ плоским числом, что и код, и потому
        // повторял его ошибку: после смены ставки оба брали новую — включая
        // счета за прошлый период. Регистр читается по ИМЕНИ через ITotalsManager:
        // типовой зависимости от модели локализации это не создаёт.
        var issued = await DocumentManager.GetDocumentAsync<SalesInvoice>(invoice.MetaId);
        var vatRate = issued?.TaxRateApplied ?? 0m;
        Assert.IsTrue(vatRate > 0m,
            "выставление обязано зафиксировать на счёте действующую ставку налога, факт {0}", vatRate);
        var vat = await TotalAsync("VatPayable", "Amount") - vat0;
        Assert.IsTrue(vat == Math.Round(300m * vatRate, 2, MidpointRounding.AwayFromZero),
            "НДС = 300 × {0} , а не {1}", vatRate, vat);

        // ── ИТОГ ЦИКЛА ───────────────────────────────────────────────────────
        // 60 закуплено − 3 списано − 12 продано = 45 штук в наличии, из них
        // 24 на MAIN и 21 на SHOP. Перемещение на итог не влияет.
        Assert.IsTrue(mainAfterSale + shopAfterWriteOff == 45m,
            "в наличии обязано остаться 45 штук, а осталось {0}", mainAfterSale + shopAfterWriteOff);

        // ── СЕБЕСТОИМОСТЬ СХОДИТСЯ СО СКЛАДОМ ────────────────────────────────
        // Расходную ногу себестоимости пишет НЕ документ, а драйвер итогов
        // CostingIssue на регистре Stock: он схлопывает движения документа по
        // товару и списывает только ЧИСТЫЙ минус. Поэтому закупка (+60) и
        // перемещение (−24/+24) себестоимость не трогают, а списание (−3) и
        // продажа (−12) трогают — ровно на 15 штук и 150 денег.
        var fifoQty = await FifoAsync("Quantity", s.Item);
        var fifoAmount = await FifoAsync("Amount", s.Item);
        Assert.IsTrue(fifoQty == 45m,
            "слои FIFO обязаны сойтись со складом: 60 − 3 − 12 = 45, а не {0}", fifoQty);
        Assert.IsTrue(fifoAmount == 450m,
            "сумма слоёв FIFO = 45 штук × 10 = 450, а не {0}", fifoAmount);

        var invQtyEnd = await TotalAsync("InventoryValue", "Qty") - invQty0;
        var invValueEnd = await TotalAsync("InventoryValue", "Value") - invValue0;
        Assert.IsTrue(invQtyEnd == 45m,
            "количество запаса = 45 штук, а не {0}", invQtyEnd);
        Assert.IsTrue(invValueEnd == 450m,
            "стоимость запаса = 450, а не {0}", invValueEnd);
        // Два регистра себестоимости считаются РАЗНЫМИ путями (движок партий
        // против прямого движения), поэтому их равенство — не тавтология: оно и
        // есть проверка, что списание не разъехалось между ними.
        Assert.IsTrue(invValueEnd == fifoAmount,
            "оценка запаса и сумма слоёв обязаны совпасть: {0} против {1}", invValueEnd, fifoAmount);
        // Цена за штуку не изменилась выбытием — снималось ровно по 10.
        Assert.IsTrue(invValueEnd / invQtyEnd == 10m,
            "цена за штуку после выбытия всё те же 450/45 = 10, а не {0}", invValueEnd / invQtyEnd);
    }

    // ──────────────────────────────── отчёт ──────────────────────────────────

    [IntegrationTest("Отчёт по остаткам в разрезе складов показывает те же числа")]
    public async Task StockReportMatchesRegisters()
    {
        var s = await SetupAsync();

        var order = await NewOrderAsync(s, boxes: 5m);
        await ReceiveAsync(order);

        var transfer = await NewTransferAsync(s, boxes: 2m);
        transfer.Subtype = StockTransfer.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(transfer);

        var writeOff = await NewWriteOffAsync(s, pieces: 3m);
        writeOff.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(writeOff);

        var invoice = await NewInvoiceAsync(s, boxes: 1m);
        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);

        // Балансовая таблица — то, с чем отчёт обязан совпасть.
        Assert.IsTrue(await StockAsync(s.MainCell, s.Item) == 24m, "MAIN = 24 по регистру");
        Assert.IsTrue(await StockAsync(s.ShopCell, s.Item) == 21m, "SHOP = 21 по регистру");

        // Отчёт строится по ВЛОЖЕННОМУ пути Cell → StoreZone → Store: регистр
        // ведётся по ячейке, а пользователь спрашивает «сколько на складе».
        var registers = await GetService<IMetadataService>().GetAllRegistersAsync();
        var stock = registers.First(r => r.Name == "Stock");

        var rows = await GetService<RegisterReportService>().BuildAsync(stock.MetaId, new RegisterReportRequest
        {
            GroupBy = new List<string> { "Cell.StoreZone.Store" },
            // Регистр общий на весь стенд — без фильтра по товару в отчёт попали
            // бы строки соседних прогонов.
            Filters = new Dictionary<string, string?> { ["Item"] = s.Item.ToString() },
        });

        Assert.IsTrue(rows.Count == 2, "в отчёте обязано быть 2 склада, а не {0}", rows.Count);

        var byStore = new Dictionary<Guid, Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var key = row["Cell.StoreZone.Store"];
            byStore[key is Guid g ? g : Guid.Parse(Convert.ToString(key)!)] = row;
        }

        Assert.IsTrue(byStore.ContainsKey(s.MainStore), "склад MAIN обязан быть строкой отчёта");
        Assert.IsTrue(byStore.ContainsKey(s.ShopStore), "склад SHOP обязан быть строкой отчёта");

        // MAIN: приход 60, расход 24 (перемещение) + 12 (продажа) = 36, остаток 24.
        var main = byStore[s.MainStore];
        Assert.IsTrue(Num(main, "Qty.In") == 0m, "у MAIN нет входящего остатка, а отчёт даёт {0}", Num(main, "Qty.In"));
        Assert.IsTrue(Num(main, "Qty.Income") == 60m, "приход MAIN = 60, а отчёт даёт {0}", Num(main, "Qty.Income"));
        Assert.IsTrue(Num(main, "Qty.Outcome") == 36m, "расход MAIN = 24 + 12 = 36, а отчёт даёт {0}", Num(main, "Qty.Outcome"));
        Assert.IsTrue(Num(main, "Qty.Out") == 24m, "остаток MAIN = 24, а отчёт даёт {0}", Num(main, "Qty.Out"));

        // SHOP: приход 24 (перемещение), расход 3 (списание), остаток 21.
        var shop = byStore[s.ShopStore];
        Assert.IsTrue(Num(shop, "Qty.Income") == 24m, "приход SHOP = 24, а отчёт даёт {0}", Num(shop, "Qty.Income"));
        Assert.IsTrue(Num(shop, "Qty.Outcome") == 3m, "расход SHOP = 3, а отчёт даёт {0}", Num(shop, "Qty.Outcome"));
        Assert.IsTrue(Num(shop, "Qty.Out") == 21m, "остаток SHOP = 21, а отчёт даёт {0}", Num(shop, "Qty.Out"));

        // Отчёт и балансовая таблица обязаны сходиться до копейки — расхождение
        // между ними значило бы, что движения и итоги живут отдельными жизнями.
        Assert.IsTrue(Num(main, "Qty.Out") + Num(shop, "Qty.Out") == 45m,
            "по отчёту в наличии 45 штук, а получилось {0}", Num(main, "Qty.Out") + Num(shop, "Qty.Out"));

        // Ни одна строка отчёта не считает товар в ЯЩИКАХ: 5 ящиков в приходе
        // означали бы, что нормализация до отчёта не доехала.
        Assert.IsTrue(Num(main, "Qty.Income") != 5m, "приход MAIN не может быть 5 — это ящики, а не штуки");
    }

    private static decimal Num(Dictionary<string, object?> row, string column)
        => row.TryGetValue(column, out var v) && v != null ? Convert.ToDecimal(v) : 0m;
}
