using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (Currency, Item, SalesInvoice, SalesInvoiceLinesTablePartRow…).
// Тестовым скриптам этот namespace НЕ приходит глобальным using'ом: без него
// `Currency` связывается с посторонним недоступным типом, и ошибка (CS0122)
// описывает не ту причину.
using ZuloOne.Runtime.Generated;

// Integration coverage for the Sales core: issuing an invoice ships stock out and
// recognizes revenue; issuing beyond on-hand is rejected by the Stock guard.
//
// Всё идёт ЧЕРЕЗ МЕНЕДЖЕРЫ типизированными сущностями — той же дверью, что и
// бизнес-код: справочник это NewRecord<T> → заполнить → SaveRecordAsync, документ —
// NewDocumentAsync<T> → строки как <T>LinesTablePartRow → SaveDocumentAsync, а
// проведение — присваивание подтипа плюс сохранение.
public class SalesFlowTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Customer;
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
        legalEntity.RegistrationNumber = "REG-SALES-1";
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
        cellType.Code = $"PICK-{Guid.NewGuid():N}"[..12];
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

        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = "Piece";
        unit.Code = "PCS";
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "GOODS";
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Gadget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Customer = customer.MetaId };
    }

    /// <summary>Остаток ячейки по номенклатуре — срез регистра Stock по обоим измерениям.</summary>
    private static Task<decimal> StockAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item });

    /// <summary>Revenue несёт только АНАЛИТИКИ, физических измерений нет — баланс
    /// рассыпается по аналитическим строкам, поэтому суммируем.</summary>
    private static async Task<decimal> RevenueAsync()
    {
        decimal sum = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("Revenue")) sum += Convert.ToDecimal(r["Amount"]);
        return sum;
    }

    private async Task StockInAsync(Setup s, decimal qty)
    {
        // Подтип не передаём: NewDocumentAsync обязан подставить НАЧАЛЬНЫЙ подтип типа (Draft).
        var adjustment = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        adjustment.Cell = s.Location;
        adjustment.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = qty });
        await DocumentManager.SaveDocumentAsync(adjustment);

        adjustment.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(adjustment);
    }

    private async Task<SalesInvoice> NewInvoiceAsync(Setup s, decimal qty, decimal price)
    {
        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = s.Customer;
        invoice.Location = s.Location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(invoice);
        return invoice;
    }

    [IntegrationTest("Выставление счёта списывает из Stock и признаёт выручку")]
    public async Task IssueShipsAndRecognizesRevenue()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);

        var invoice = await NewInvoiceAsync(s, qty: 3m, price: 5m);

        // Черновик ничего не отгружает и выручки не признаёт. Проверка ДО перехода
        // обязательна: SalesInvoice объявлен postOnSave, и без неё утверждения ниже
        // зеленели бы независимо от того, сделал ли переход Draft → Issued хоть что-то.
        Assert.IsTrue(await StockAsync(s) == 10m, "черновик счёта не должен трогать остаток, факт {0}", await StockAsync(s));
        Assert.IsTrue(await RevenueAsync() == 0m, "черновик счёта не должен признавать выручку, факт {0}", await RevenueAsync());

        // Выставление — переход подтипа, то есть присваивание плюс сохранение
        // (MIQS doc.SubtypeID = …; SaveDocument(doc)).
        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);

        var stock = await StockAsync(s);
        var revenue = await RevenueAsync();

        Assert.IsTrue(stock == 7m, "остаток ячейки должен стать 7 (10 − 3), а не {0}", stock);
        Assert.IsTrue(revenue == 15m, "выручка должна быть 15 (3 × 5), а не {0}", revenue);
    }

    [IntegrationTest("Продажа сверх остатка отклоняется")]
    public async Task OverSellIsRejected()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);

        var invoice = await NewInvoiceAsync(s, qty: 20m, price: 5m); // only 10 on hand

        var rejected = false;
        try
        {
            invoice.Subtype = SalesInvoice.Subtypes.Issued;
            await DocumentManager.SaveDocumentAsync(invoice);
            // Сюда попадаем, только если охранник НЕ бросил: тогда отказ должен быть
            // виден по регистру. После броска базу не трогаем — бросок портит
            // объемлющую транзакцию прогона.
            rejected = await RevenueAsync() == 0m; // posting blocked → no revenue recognized
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "продажа 20 при остатке 10 должна быть отклонена");
    }
}
