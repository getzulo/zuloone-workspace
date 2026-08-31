using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (Currency, Item, SalesInvoice, SalesInvoiceLinesTablePartRow…).
// Тестовым скриптам этот namespace НЕ приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// Покрытие локализации КСА: выставление Sales-инвойса начисляет НДС 15% в
// регистр VatPayable (ставка берётся из глобальной константы SaudiVatRate).
//
// Данные строятся типизированно через менеджеры: справочники — NewRecord<T> →
// SaveRecordAsync, счёт — NewDocumentAsync<SalesInvoice> → SalesInvoiceLinesTablePartRow
// → SaveDocumentAsync, выставление — присваивание подтипа плюс сохранение.
public class SaudiVatFlowTest : IntegrationTestScriptBase
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
        currency.Name = "Riyal";
        currency.Code = "SAR";
        currency.Symbol = "﷼";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Saudi Arabia";
        country.CodeISO2 = "SA";
        country.CodeISO3 = "SAU";
        country.PhoneCode = "966";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "Riyadh Trading";
        legalEntity.RegistrationNumber = "REG-SA-1";
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

    // VatPayable несёт одну динамическую аналитику (Customer) и ни одного
    // физического измерения — баланс рассыпается по аналитическим строкам, поэтому
    // суммируем.
    private static async Task<decimal> VatAsync()
    {
        decimal sum = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("VatPayable")) sum += Convert.ToDecimal(r["Amount"]);
        return sum;
    }

    [IntegrationTest("Выставление счёта начисляет НДС 15% в VatPayable")]
    public async Task IssueAccruesVat()
    {
        var s = await SetupAsync();

        // Остаток заводится движением ВНЕ документа — это осознанный срез: тест про
        // налог, а не про приход. ITotalsManager требует назвать владельца движения
        // явно, и null здесь означает «ничей», ровно как было.
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 20m });

        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = s.Customer;
        invoice.Location = s.Location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 10m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(invoice);

        // Черновик налога не начисляет. Проверка ДО перехода обязательна:
        // SalesInvoice объявлен postOnSave, и без неё «НДС 15» ниже подтвердилось бы
        // даже если переход Draft → Issued не сделал ничего.
        Assert.IsTrue(await VatAsync() == 0m, "черновик счёта не должен начислять НДС, факт {0}", await VatAsync());

        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);

        // База 10 × 10 = 100; НДС 15% = 15.
        var vat = await VatAsync();
        Assert.IsTrue(vat == 15m, "НДС 15 при базе 100, факт {0}", vat);
    }
}
