using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей. Тест-скриптам этот namespace НЕ приходит
// глобальным using'ом.
using ZuloOne.Runtime.Generated;

// ВЫБЫТИЕ ЗАПАСОВ МИМО ПРОДАЖИ ОБЯЗАНО ПОПАДАТЬ В КНИГУ.
//
// Приход дебетовал счёт запасов, продажа кредитовала его через себестоимость, а
// бой, недостача и отпуск не попадали в книгу вообще: стоимость уходила из
// регистра и оставалась на счёте запасов навсегда. Книга завышала запас ровно на
// всё списанное за историю.
//
// Проверка идёт ПО СЧЕТАМ — через строки JournalEntry, связанные с документом.
// Сумма по всей книге сошлась бы всегда (это инвариант двойной записи) и прошла
// бы даже при разноске не на те счета.
public class InventoryWriteOffGLTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Cell;
        public Guid Item;
        public Guid Supplier;
        public Guid Customer;
        public Guid InventoryAccount;
        public Guid WriteOffAccount;
        public Guid SurplusAccount;
        public Guid PayableAccount;
        public Guid CashAccount;
        public Guid LegalEntity;
    }

    private async Task<Setup> SetupAsync(
        bool configureWriteOffAccount = true,
        bool? configureSurplusAccount = null)
    {
        var today = DateTime.UtcNow.Date;

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
        legalEntity.RegistrationNumber = $"REG-WO-{Db.NewId():N}"[..16];
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
        cellType.Code = $"WO-{Db.NewId():N}"[..12];
        cellType.Name = "Storage";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "A-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

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
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Corner Shop";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var inventoryAccount = await NewAccountAsync("1400", "Inventory", AccountType.Asset, currency.MetaId);
        var writeOffAccount = await NewAccountAsync("7100", "Inventory write-off", AccountType.Expense, currency.MetaId);
        var surplusAccount = await NewAccountAsync("9100", "Inventory surplus", AccountType.Income, currency.MetaId);
        var payableAccount = await NewAccountAsync("2000", "Accounts payable", AccountType.Liability, currency.MetaId);
        var cashAccount = await NewAccountAsync("1000", "Cash", AccountType.Asset, currency.MetaId);

        // Излишек настраивается вместе со списанием: обе ноги одного контура.
        // Явный configureSurplusAccount нужен кейсу «счёт излишка не задан».
        var surplusConfigured = configureSurplusAccount ?? configureWriteOffAccount;

        // Настройки — ОДИНОЧНЫЙ и КЭШИРУЕМЫЙ справочник: правим существующую
        // запись, если она есть, иначе заводим. Слепой NewRecord делает тест
        // зависимым от порядка прогона.
        var accRows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        var settings = accRows.Count > 0 ? accRows[0] : DictionaryManager.NewRecord<AccountingSettings>();
        settings.InventoryAccountCode = "1400";
        settings.PayableAccountCode = "2000";
        settings.InventoryWriteOffAccountCode = configureWriteOffAccount ? "7100" : null;
        settings.InventorySurplusAccountCode = surplusConfigured ? "9100" : null;
        settings.CashAccountCode = "1000";
        await DictionaryManager.SaveRecordAsync(settings);

        var fiscalYear = DictionaryManager.NewRecord<FiscalYear>();
        fiscalYear.Code = "FY";
        fiscalYear.StartDate = today.AddMonths(-6);
        fiscalYear.EndDate = today.AddMonths(6);
        fiscalYear.IsClosed = false;
        fiscalYear = await DictionaryManager.SaveRecordAsync(fiscalYear);

        var fiscalPeriod = DictionaryManager.NewRecord<FiscalPeriod>();
        fiscalPeriod.Code = "P1";
        fiscalPeriod.FiscalYear = fiscalYear.MetaId;
        fiscalPeriod.FromDate = today.AddDays(-15);
        fiscalPeriod.ToDate = today.AddDays(15);
        fiscalPeriod.Status = "Open";
        await DictionaryManager.SaveRecordAsync(fiscalPeriod);

        return new Setup
        {
            Cell = cell.MetaId,
            Item = item.MetaId,
            Supplier = supplier.MetaId,
            Customer = customer.MetaId,
            InventoryAccount = inventoryAccount,
            WriteOffAccount = writeOffAccount,
            SurplusAccount = surplusAccount,
            PayableAccount = payableAccount,
            CashAccount = cashAccount,
            LegalEntity = legalEntity.MetaId,
        };
    }

    private static async Task<Guid> NewAccountAsync(string code, string name, AccountType type, Guid currency)
    {
        var account = DictionaryManager.NewRecord<ChartOfAccounts>();
        account.Code = code;
        account.Name = name;
        account.AccountType = type;
        account.IsPostable = true;
        account.Currency = currency;
        return (await DictionaryManager.SaveRecordAsync(account)).MetaId;
    }

    /// <summary>Приход: заказ поставщику объявленным маршрутом. Он и заводит партии.</summary>
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

    private static async Task<StockAdjustment> AdjustAsync(Setup s, decimal qty, string reason)
    {
        var doc = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        doc.Cell = s.Cell;
        doc.Reason = reason;
        doc.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = qty });
        await DocumentManager.SaveDocumentAsync(doc);

        doc.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }

    /// <summary>Дебет/кредит ОДНОГО счёта по проводкам, порождённым документом.</summary>
    private static async Task<(decimal Debit, decimal Credit)> AccountAsync(Guid document, Guid account)
    {
        decimal debit = 0m, credit = 0m;

        var family = await DocumentManager.GetDocumentFamilyAsync(document);
        var children = family.Edges.Where(e => e.ParentDocId == document).Select(e => e.ChildDocId).Distinct();

        foreach (var childId in children)
        {
            var entry = await DocumentManager.GetDocumentAsync<JournalEntry>(childId);
            if (entry == null) continue;
            foreach (var line in entry.Lines.Where(l => l.Account == account))
            {
                debit += line.Debit;
                credit += line.Credit;
            }
        }

        return (debit, credit);
    }

    [IntegrationTest("Списание запасов разносится в книгу: Dr списание / Cr запасы")]
    public async Task WriteOffPostsToLedger()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);   // партия: 10 штук по 7 = 70

        var doc = await AdjustAsync(s, -3m, "Бой при выкладке");

        // 3 штуки по 7 = 21 — ровно то, что снял с партий драйвер Costing.
        var writeOff = await AccountAsync(doc.MetaId, s.WriteOffAccount);
        Assert.IsTrue(writeOff.Debit == 21m,
            "списание дебетует счёт списания на 3 × 7 = 21, факт {0}", writeOff.Debit);

        var inventory = await AccountAsync(doc.MetaId, s.InventoryAccount);
        Assert.IsTrue(inventory.Credit == 21m,
            "и кредитует счёт запасов на ту же сумму, факт {0}", inventory.Credit);
    }

    [IntegrationTest("Излишек разносится в книгу: Dr запасы / Cr доход от излишка")]
    public async Task SurplusPostsToLedger()
    {
        // Излишек — доход, а не сторно списания. Счёт потерь должен остаться
        // нулевым: находка не затирает бой, маржа и статья потерь остаются чистыми.
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);   // партия: 10 штук по 7 = 70

        var doc = await AdjustAsync(s, 5m, "Излишек при пересчёте");

        // Costing заводит FIFO 5 × 7 = 35 — это и есть сумма в книге.
        var inventory = await AccountAsync(doc.MetaId, s.InventoryAccount);
        Assert.IsTrue(inventory.Debit == 35m,
            "излишек дебетует запасы на 5 × 7 = 35, факт {0}", inventory.Debit);

        var surplus = await AccountAsync(doc.MetaId, s.SurplusAccount);
        Assert.IsTrue(surplus.Credit == 35m,
            "и кредитует доход от излишка на ту же сумму, факт {0}", surplus.Credit);

        var writeOff = await AccountAsync(doc.MetaId, s.WriteOffAccount);
        Assert.IsTrue(writeOff.Debit == 0m,
            "приход на склад не является списанием, факт {0}", writeOff.Debit);
    }

    [IntegrationTest("Инвентаризация вверх разносит излишек в книгу")]
    public async Task StockCountUpPostsSurplusToLedger()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);

        // Draft → Posted: движения склада пишет Tx, партию — Costing OnAfterPost,
        // GL читает ItemCostFifo, а не QtyDelta.
        var count = await DocumentManager.NewDocumentAsync<StockCount>();
        count.Cell = s.Cell;
        count.CountDate = DateTime.UtcNow.Date;
        count.Lines.Add(new StockCountLinesTablePartRow { Item = s.Item, CountedQty = 13m });
        await DocumentManager.SaveDocumentAsync(count);

        count.Subtype = StockCount.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(count);

        // 3 штуки по 7 = 21 — ровно то, что Costing записал в партию излишка.
        var inventory = await AccountAsync(count.MetaId, s.InventoryAccount);
        Assert.IsTrue(inventory.Debit == 21m,
            "пересчёт вверх дебетует запасы на 3 × 7 = 21, факт {0}", inventory.Debit);

        var surplus = await AccountAsync(count.MetaId, s.SurplusAccount);
        Assert.IsTrue(surplus.Credit == 21m,
            "и кредитует доход от излишка на ту же сумму, факт {0}", surplus.Credit);

        var writeOff = await AccountAsync(count.MetaId, s.WriteOffAccount);
        Assert.IsTrue(writeOff.Debit == 0m,
            "пересчёт вверх не списывает, факт {0}", writeOff.Debit);
    }

    [IntegrationTest("Без настроенного счёта излишка документ проводится, проводки нет")]
    public async Task UnconfiguredSurplusAccountDoesNotBreakPosting()
    {
        // Best-effort: ненастроенный доход от излишка не роняет складскую операцию.
        var s = await SetupAsync(configureSurplusAccount: false);
        await ReceiveAsync(s, 10m, 7m);

        var doc = await AdjustAsync(s, 5m, "Излишек");

        var stored = await DocumentManager.GetDocumentAsync<StockAdjustment>(doc.MetaId);
        Assert.IsTrue(stored?.Subtype == StockAdjustment.Subtypes.Posted,
            "излишек проведён несмотря на ненастроенный счёт, факт {0}", stored?.Subtype);

        var surplus = await AccountAsync(doc.MetaId, s.SurplusAccount);
        Assert.IsTrue(surplus.Debit == 0m && surplus.Credit == 0m,
            "без счёта излишка проводки нет, дебет {0} кредит {1}", surplus.Debit, surplus.Credit);
    }

    [IntegrationTest("Излишек без истории закупок в книгу не идёт — партии нулевые")]
    public async Task ZeroCostSurplusPostsNothingToLedger()
    {
        // Costing заводит партию с Amount=0: цены взяться неоткуда. Выдумывать
        // доход в книге не из чего — GL тоже молчит. Тот же инвариант, что
        // SurplusWithoutHistoryIsZeroCost у себестоимости.
        var s = await SetupAsync();

        var doc = await AdjustAsync(s, 5m, "Излишек без истории");

        var inventory = await AccountAsync(doc.MetaId, s.InventoryAccount);
        Assert.IsTrue(inventory.Debit == 0m && inventory.Credit == 0m,
            "нулевая партия не двигает запасы в книге, дебет {0} кредит {1}",
            inventory.Debit, inventory.Credit);

        var surplus = await AccountAsync(doc.MetaId, s.SurplusAccount);
        Assert.IsTrue(surplus.Debit == 0m && surplus.Credit == 0m,
            "и доход от излишка тоже ноль, дебет {0} кредит {1}",
            surplus.Debit, surplus.Credit);
    }

    [IntegrationTest("Отпуск со склада разносится в книгу")]
    public async Task GoodsIssuePostsToLedger()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);

        var issue = await DocumentManager.NewDocumentAsync<GoodsIssue>();
        issue.FromCell = s.Cell;
        issue.Customer = s.Customer;
        issue.Lines.Add(new GoodsIssueLinesTablePartRow { Item = s.Item, Quantity = 2m });
        await DocumentManager.SaveDocumentAsync(issue);

        issue.Subtype = GoodsIssue.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(issue);

        var writeOff = await AccountAsync(issue.MetaId, s.WriteOffAccount);
        Assert.IsTrue(writeOff.Debit == 14m,
            "отпуск 2 штук по 7 = 14, факт {0}", writeOff.Debit);
    }

    [IntegrationTest("Оприходование заказа разносится в книгу: Dr запасы / Cr кредиторка")]
    public async Task PurchaseReceiptPostsToLedger()
    {
        // Код разноски закупки существовал с самого начала, но не был подтверждён
        // ни одним тестом: продажи и ФОТ свои проводки проверяли, закупка — нет.
        // Соседние тесты этого файла заводят полный профиль счетов, так что
        // проверка стоит ровно одного сценария.
        var s = await SetupAsync();

        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Cell;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 10m, UnitPrice = 7m });
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);

        // До оприходования книга о заказе ничего не знает.
        Assert.IsTrue((await AccountAsync(order.MetaId, s.InventoryAccount)).Debit == 0m,
            "размещённый заказ не дебетует запасы");

        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);

        var inventory = await AccountAsync(order.MetaId, s.InventoryAccount);
        Assert.IsTrue(inventory.Debit == 70m,
            "оприходование дебетует запасы на 10 × 7 = 70, факт {0}", inventory.Debit);

        var payable = await AccountAsync(order.MetaId, s.PayableAccount);
        Assert.IsTrue(payable.Credit == 70m,
            "и кредитует кредиторку на ту же сумму, факт {0}", payable.Credit);
    }

    [IntegrationTest("Оплата поставщику дебетует кредиторку — счёт в книге закрывается")]
    public async Task VendorPaymentClosesPayableInLedger()
    {
        // ДЫРА, ОСТАВЛЕННАЯ ВМЕСТЕ С САМИМ ДОКУМЕНТОМ ОПЛАТЫ. Оприходование
        // кредитует счёт кредиторки, а дебетовать его было нечем: в регистре
        // Payable долг гасился, в книге рос бесконечно. Та же пара уже сделана у
        // выплаты ФОТ и у платежа в фонд — здесь она закрывает закупочный контур.
        var s = await SetupAsync();

        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Cell;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 10m, UnitPrice = 7m });
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);

        var accrued = await AccountAsync(order.MetaId, s.PayableAccount);
        Assert.IsTrue(accrued.Credit == 70m, "приход признал долг в книге, факт {0}", accrued.Credit);

        var payment = await DocumentManager.NewDocumentAsync<VendorPayment>();
        payment.LegalEntity = s.LegalEntity;
        payment.Lines.Add(new VendorPaymentLinesTablePartRow { Supplier = s.Supplier, Amount = 70m });
        await DocumentManager.SaveDocumentAsync(payment);

        // Черновик оплаты книгу не трогает.
        Assert.IsTrue((await AccountAsync(payment.MetaId, s.PayableAccount)).Debit == 0m,
            "черновик оплаты не дебетует кредиторку");

        payment.Subtype = VendorPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        var paid = await AccountAsync(payment.MetaId, s.PayableAccount);
        Assert.IsTrue(paid.Debit == 70m,
            "оплата дебетует кредиторку на 70, факт {0}", paid.Debit);

        var cash = await AccountAsync(payment.MetaId, s.CashAccount);
        Assert.IsTrue(cash.Credit == 70m,
            "и кредитует денежные средства на ту же сумму, факт {0}", cash.Credit);

        // Итог: счёт кредиторки в книге нетто-ноль, как и регистр Payable.
        Assert.IsTrue(accrued.Credit - paid.Debit == 0m,
            "счёт кредиторки закрыт: 70 − 70 = 0, факт {0}", accrued.Credit - paid.Debit);
    }

    [IntegrationTest("Оплата поставщику без юрлица проводится, но в книгу не идёт")]
    public async Task VendorPaymentWithoutLegalEntitySkipsLedger()
    {
        // Юрлицо у платежа необязательно: вывести его не из чего (у поставщика
        // связи с юрлицом нет), поэтому не задано — разноски нет, а сам платёж
        // обязан пройти. Та же best-effort политика, что у прочих ног.
        var s = await SetupAsync();

        var payment = await DocumentManager.NewDocumentAsync<VendorPayment>();
        payment.Lines.Add(new VendorPaymentLinesTablePartRow { Supplier = s.Supplier, Amount = 70m });
        await DocumentManager.SaveDocumentAsync(payment);
        payment.Subtype = VendorPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        var stored = await DocumentManager.GetDocumentAsync<VendorPayment>(payment.MetaId);
        Assert.IsTrue(stored?.Subtype == VendorPayment.Subtypes.Paid,
            "платёж проведён, факт {0}", stored?.Subtype);
        Assert.IsTrue((await AccountAsync(payment.MetaId, s.PayableAccount)).Debit == 0m,
            "без юрлица проводки нет");
    }

    [IntegrationTest("Без настроенного счёта списания документ проводится как прежде")]
    public async Task UnconfiguredAccountDoesNotBreakPosting()
    {
        // Разноска best-effort: ненастроенный профиль счетов не должен ронять
        // складскую операцию — она к бухгалтерии отношения не имеет.
        var s = await SetupAsync(configureWriteOffAccount: false);
        await ReceiveAsync(s, 10m, 7m);

        var doc = await AdjustAsync(s, -3m, "Бой");

        var stored = await DocumentManager.GetDocumentAsync<StockAdjustment>(doc.MetaId);
        Assert.IsTrue(stored?.Subtype == StockAdjustment.Subtypes.Posted,
            "списание проведено несмотря на ненастроенный счёт, факт {0}", stored?.Subtype);

        var writeOff = await AccountAsync(doc.MetaId, s.WriteOffAccount);
        Assert.IsTrue(writeOff.Debit == 0m, "проводки нет, факт {0}", writeOff.Debit);

        // И сам склад отработал.
        var stock = await TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Item"] = s.Item, ["Cell"] = s.Cell });
        Assert.IsTrue(stock == 7m, "остаток 10 − 3 = 7, факт {0}", stock);
    }
}
