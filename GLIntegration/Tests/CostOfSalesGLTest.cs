using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей. Тестовым скриптам это пространство имён НЕ
// приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// СЕБЕСТОИМОСТЬ ПРОДАННОГО В ГЛАВНОЙ КНИГЕ.
//
// Продажа порождает в GL ДВЕ пары, а не одну: выручку (Dr дебиторка / Cr выручка)
// и себестоимость (Dr себестоимость / Cr запасы). Без второй пары книга врала бы
// в обе стороны сразу — прибыль завышена на всю себестоимость, а запасы висят
// активом, которого на складе уже нет.
//
// Сумму себестоимости обработчик НЕ считает сам: её посчитал и записал драйвер
// CostingIssue, когда уменьшился складской остаток. Поэтому тест ведёт себя как
// пользователь — приходует заказом поставщику и продаёт счётом, ни одной прямой
// проводки, — и требует, чтобы число в книге совпало с числом, ушедшим из слоёв
// себестоимости. Совпадение этих двух чисел и есть проверяемое утверждение:
// склад и главная книга не разъезжаются.
//
// Второй сценарий — товар, заведённый прямым движением регистра (так делают
// демо-данные и часть тестов): партий у него нет, списывать нечего, и проводка
// себестоимости не должна появляться ВОВСЕ. Ноль — это не «ошибка тихо съедена»,
// а корректный учёт: актива с себестоимостью не было.
public class CostOfSalesGLTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Cell;
        public Guid Item;
        public Guid Customer;
        public Guid Supplier;
    }

    // ───────────────────────────── мастер-данные ─────────────────────────────

    private async Task<Setup> SetupAsync(string tag)
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
        legalEntity.RegistrationNumber = $"REG-COGS-{tag}";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "SP";
        divisionType.Name = "SalesPoint";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Shop";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Shop WH";
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
        cellType.Code = $"PICK-{Db.NewId():N}"[..12];
        cellType.Name = "Picking";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "P-01";
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
        group.Code = $"GOODS-{Db.NewId():N}"[..12];
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Gadget";
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

        await NewAccountAsync("1200", "Accounts receivable", AccountType.Asset, currency.MetaId);
        await NewAccountAsync("4000", "Sales revenue", AccountType.Income, currency.MetaId);
        await NewAccountAsync("1400", "Inventory", AccountType.Asset, currency.MetaId);
        await NewAccountAsync("5000", "Cost of goods sold", AccountType.Expense, currency.MetaId);

        // Профиль разноски: пятый код — счёт себестоимости, без него вторая пара
        // не разносится (best-effort, как и остальные).
        var settings = DictionaryManager.NewRecord<AccountingSettings>();
        settings.ArAccountCode = "1200";
        settings.RevenueAccountCode = "4000";
        settings.InventoryAccountCode = "1400";
        settings.CogsAccountCode = "5000";
        settings.PayableAccountCode = "2000";
        await DictionaryManager.SaveRecordAsync(settings);

        var fiscalYear = DictionaryManager.NewRecord<FiscalYear>();
        fiscalYear.Code = "FY";
        fiscalYear.StartDate = today.AddMonths(-6);
        fiscalYear.EndDate = today.AddMonths(6);
        fiscalYear.IsClosed = false;
        fiscalYear = await DictionaryManager.SaveRecordAsync(fiscalYear);

        var period = DictionaryManager.NewRecord<FiscalPeriod>();
        period.Code = "P1";
        period.FiscalYear = fiscalYear.MetaId;
        period.FromDate = today.AddDays(-15);
        period.ToDate = today.AddDays(15);
        period.Status = "Open";
        await DictionaryManager.SaveRecordAsync(period);

        // Метод оценки задаём ЯВНО: иначе тест проверял бы «что настроено на
        // стенде», а числа себестоимости зависят от метода.
        var costing = await DictionaryManager.GetRecordsAsync<CostingSettings>(null, 1);
        var cs = costing.Count > 0 ? costing[0] : DictionaryManager.NewRecord<CostingSettings>();
        cs.CostingMethod = "FIFO";
        await DictionaryManager.SaveRecordAsync(cs);

        return new Setup
        {
            Cell = cell.MetaId,
            Item = item.MetaId,
            Customer = customer.MetaId,
            Supplier = supplier.MetaId,
        };
    }

    private static async Task NewAccountAsync(string code, string name, AccountType type, Guid currency)
    {
        var account = DictionaryManager.NewRecord<ChartOfAccounts>();
        account.Code = code;
        account.Name = name;
        account.AccountType = type;
        account.IsPostable = true;
        account.Currency = currency;
        await DictionaryManager.SaveRecordAsync(account);
    }

    // ─────────────────────────────── действия ────────────────────────────────

    /// <summary>Приход объявленным маршрутом Draft → Ordered → Received: слои
    /// себестоимости создаёт именно оприходование.</summary>
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

    private static async Task SellAsync(Setup s, decimal qty, decimal price)
    {
        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = s.Customer;
        invoice.Location = s.Cell;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(invoice);

        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
    }

    /// <summary>Дебет и кредит по всему регистру GL: разрезы там ДИНАМИЧЕСКИЕ
    /// (Account/LegalEntity/FiscalPeriod), точечный срез не адресуется.</summary>
    private static async Task<(decimal Debit, decimal Credit)> LedgerAsync()
    {
        decimal debit = 0m, credit = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("GL"))
        {
            debit += Convert.ToDecimal(r["Debit"]);
            credit += Convert.ToDecimal(r["Credit"]);
        }
        return (debit, credit);
    }

    private static Task<decimal> FifoAsync(string resource, Guid item)
        => TotalsManager.GetBalanceAsync("ItemCostFifo", resource,
            new Dictionary<string, object?> { ["Item"] = item });

    // ─────────────────────────────── сценарии ────────────────────────────────

    [IntegrationTest("Продажа разносит себестоимость: Dr себестоимость / Cr запасы")]
    public async Task SalePostsCostOfGoodsSold()
    {
        var s = await SetupAsync("1");

        // Два лота по разной цене: 10×7 = 70 и 10×9 = 90. Продаём 15 — FIFO гасит
        // старейший целиком и второй частично: 10×7 + 5×9 = 115.
        await ReceiveAsync(s, 10m, 7m);
        await ReceiveAsync(s, 10m, 9m);

        var (debit0, credit0) = await LedgerAsync();
        var costBefore = await FifoAsync("Amount", s.Item);

        await SellAsync(s, 15m, 20m);

        // Сколько РЕАЛЬНО ушло из слоёв себестоимости — вторая величина, с которой
        // обязана совпасть книга. Считается фактом, а не повторением формулы.
        var written = costBefore - await FifoAsync("Amount", s.Item);
        Assert.IsTrue(written == 115m,
            "FIFO списал 10×7 + 5×9 = 115, факт {0} (средняя дала бы 120)", written);

        var (debit, credit) = await LedgerAsync();
        var dr = debit - debit0;
        var cr = credit - credit0;

        // Выручка 15 × 20 = 300, себестоимость 115 — в книге обе пары.
        Assert.IsTrue(dr == 300m + written,
            "дебет GL = выручка 300 + себестоимость {0} = {1}, факт {2}", written, 300m + written, dr);
        Assert.IsTrue(cr == 300m + written,
            "кредит GL = выручка 300 + запасы {0} = {1}, факт {2}", written, 300m + written, cr);
        Assert.IsTrue(dr == cr, "проводки сбалансированы: дебет {0} = кредит {1}", dr, cr);
    }

    [IntegrationTest("Товар без партий продаётся без проводки себестоимости")]
    public async Task SaleWithoutCostLayersPostsRevenueOnly()
    {
        var s = await SetupAsync("2");

        // Остаток заведён ПРЯМЫМ движением регистра — приходом он не был, партий
        // себестоимости нет. Так живут демо-данные и часть тестов.
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Cell, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        Assert.IsTrue(await FifoAsync("Quantity", s.Item) == 0m,
            "прямое движение склада партий не создаёт, факт {0}", await FifoAsync("Quantity", s.Item));

        var (debit0, credit0) = await LedgerAsync();

        await SellAsync(s, 4m, 25m);

        var (debit, credit) = await LedgerAsync();
        var dr = debit - debit0;

        // Ровно выручка 4 × 25 = 100 и ничего сверх: списывать было нечего, и
        // «на всякий случай» ноль в книгу не пишется.
        Assert.IsTrue(dr == 100m,
            "в книге только выручка 4 × 25 = 100 без себестоимости, факт {0}", dr);
        Assert.IsTrue(credit - credit0 == 100m,
            "кредит GL = 100, факт {0}", credit - credit0);
    }
}
