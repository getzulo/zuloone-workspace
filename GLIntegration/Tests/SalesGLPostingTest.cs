using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей (SalesInvoice, Currency, AccountType,
// SalesInvoiceLinesTablePartRow…). Тестовые скрипты НЕ получают это пространство
// имён глобальным using — без него `Currency` цепляется за посторонний недоступный
// тип, и ошибка компилятора описывает не ту причину.
using ZuloOne.Runtime.Generated;

// Покрытие GL-интеграции: выставление Sales-инвойса при настроенных счетах
// разноски создаёт сбалансированную проводку в главной книге
// (Dr дебиторка = Cr выручка = сумма счёта).
//
// Всё — типизированными сущностями через менеджеры: запись через
// IDictionaryManager, документ через IDocumentManager, регистр через ITotalsManager.
public class SalesGLPostingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Выставление счёта разносится в GL: Dr дебиторка = Cr выручка")]
    public async Task IssuePostsBalancedGL()
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
        legalEntity.RegistrationNumber = "REG-GL-1";
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

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"PICK-{Db.NewId():N}"[..12]; // Db.NewId() — законный остаток: генерация id.
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
        uom.Code = "PCS";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "GOODS";
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

        // Настроенные счета разноски (коды совпадают с профилем AccountingSettings).
        // AccountType — ГЕНЕРЁННЫЙ ENUM, а не строка: строковый литерал здесь просто
        // не скомпилируется.
        var receivable = DictionaryManager.NewRecord<ChartOfAccounts>();
        receivable.Code = "1200";
        receivable.Name = "Accounts receivable";
        receivable.AccountType = AccountType.Asset;
        receivable.IsPostable = true;
        receivable.Currency = currency.MetaId;
        receivable = await DictionaryManager.SaveRecordAsync(receivable);

        var revenue = DictionaryManager.NewRecord<ChartOfAccounts>();
        revenue.Code = "4000";
        revenue.Name = "Sales revenue";
        revenue.AccountType = AccountType.Income;
        revenue.IsPostable = true;
        revenue.Currency = currency.MetaId;
        revenue = await DictionaryManager.SaveRecordAsync(revenue);

        // Профиль разноски — одиночный справочник настроек учёта: именно отсюда
        // GeneralLedgerService берёт коды счетов (раньше были глобальные константы).
        var settings = DictionaryManager.NewRecord<AccountingSettings>();
        settings.ArAccountCode = "1200";
        settings.RevenueAccountCode = "4000";
        settings.InventoryAccountCode = "1400";
        settings.PayableAccountCode = "2000";
        settings = await DictionaryManager.SaveRecordAsync(settings);

        // Учётный год и период, покрывающий сегодня.
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
        period = await DictionaryManager.SaveRecordAsync(period);

        // Товар на складе. Движение вне цепочки проведения документа — редкий и
        // осознанный случай, у менеджера итогов для него отдельная дверь.
        await TotalsManager.PostMovementAsync("Stock", null, today,
            new Dictionary<string, object?> { ["Cell"] = cell.MetaId, ["Item"] = item.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        // Подтип не передаём: NewDocumentAsync подставит НАЧАЛЬНЫЙ (Draft), дальше
        // идём объявленным маршрутом Draft → Issued.
        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = customer.MetaId;
        invoice.Location = cell.MetaId;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = item.MetaId, Quantity = 3m, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(invoice);

        // Состояние ДО перехода: черновик в главную книгу не попадает. Без этого
        // утверждения ниже проходят даже тогда, когда счёт разнёсся на сохранении.
        Assert.IsTrue((await TotalsManager.QueryBalancesAsync("GL")).Count == 0,
            "черновик счёта не должен порождать проводок GL");

        // Выставление — это ПРИСВОЕНИЕ подтипа плюс сохранение.
        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);

        // GL несёт динамические аналитики (Account/LegalEntity/FiscalPeriod) — баланс
        // схлопывается, суммируем дебет и кредит по всем строкам.
        decimal debit = 0m, credit = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("GL"))
        {
            debit += Convert.ToDecimal(r["Debit"]);
            credit += Convert.ToDecimal(r["Credit"]);
        }
        Assert.IsTrue(debit == 15m, "дебет GL = 15 (дебиторка), факт {0}", debit);
        Assert.IsTrue(credit == 15m, "кредит GL = 15 (выручка), факт {0}", credit);
        Assert.IsTrue(debit == credit, "проводка сбалансирована: дебет {0} = кредит {1}", debit, credit);
    }
}
