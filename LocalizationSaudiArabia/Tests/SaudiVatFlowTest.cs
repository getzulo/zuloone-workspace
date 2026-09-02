using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (Currency, Item, SalesInvoice, SalesInvoiceLinesTablePartRow…).
// Тестовым скриптам этот namespace НЕ приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// Покрытие локализации КСА: выставление Sales-инвойса начисляет НДС в регистр
// VatPayable.
//
// СТАВКА ПРИХОДИТ ИЗ НАЛОГОВОГО КОНТУРА, а не из константы. Раньше страновой
// скрипт читал плоскую `SaudiVatRate = 0.15` без даты — второй источник истины
// рядом с датированным справочником TaxRate, из-за которого VatPayable и
// TaxLedger разошлись бы в день изменения ставки. Поэтому тест теперь ОБЯЗАН
// настроить контур целиком (налог → ставка → код → настройки): без него ставка
// на счёте нулевая и проводки нет — ровно то поведение, что было при
// отсутствующей константе.
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

    private async Task<Setup> SetupAsync(bool splitRates = false)
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

        await TaxCircuitAsync(splitRates);

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Customer = customer.MetaId };
    }

    /// <summary>
    /// Налоговый контур: налог → ставка 15% → код → настройки. Именно отсюда
    /// страновой скрипт теперь получает ставку — через поле TaxRateApplied,
    /// которое выставление счёта фиксирует на документе.
    /// </summary>
    private async Task TaxCircuitAsync(bool splitRates)
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
        // История из двух ставок: старую закрываем концом 2025, с 2026 — 20%.
        if (splitRates) rate.EffectiveTo = new DateTime(2025, 12, 31);
        rate = await DictionaryManager.SaveRecordAsync(rate);

        if (splitRates)
        {
            var next = DictionaryManager.NewRecord<TaxRate>();
            next.Tax = tax.MetaId;
            next.Code = $"R2-{Db.NewId():N}"[..10];
            next.Rate = 0.20m;
            next.EffectiveFrom = new DateTime(2026, 1, 1);
            await DictionaryManager.SaveRecordAsync(next);
        }

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

        // TaxSettings — ОДИНОЧНЫЙ и КЭШИРУЕМЫЙ справочник: кэш переживает откат
        // кейса, поэтому правим существующую запись, а не заводим слепо.
        var rows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);
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

    [IntegrationTest("Счёт задним числом считается по ставке, действовавшей в его дату")]
    public async Task BackdatedInvoiceUsesHistoricalRate()
    {
        // ЭТОТ КЕЙС И ЕСТЬ СМЫСЛ ПРАВКИ. Пока ставка бралась из плоской константы
        // SaudiVatRate, у неё не было даты вовсе: после повышения НДС до 20% ЛЮБОЙ
        // счёт — включая выставленный за прошлый период — считался бы здесь по
        // 20%, тогда как универсальный контур взял бы действовавшие 15%. Два
        // регистра разошлись бы молча.
        var s = await SetupAsync(splitRates: true);

        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 20m });

        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = s.Customer;
        invoice.Location = s.Location;
        invoice.DocumentDate = new DateTime(2024, 6, 1);   // окно старой ставки
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 10m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(invoice);

        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);

        // Ставка зафиксирована НА ДОКУМЕНТЕ — проверяется отдельно от суммы: без
        // этого провал ниже не отличить от ошибки в расчёте базы.
        var issued = await DocumentManager.GetDocumentAsync<SalesInvoice>(invoice.MetaId);
        Assert.IsTrue(issued != null && issued.TaxRateApplied == 0.15m,
            "на счёте обязана быть ставка, действовавшая 2024-06-01 (0.15), факт {0}",
            issued?.TaxRateApplied);

        var vat = await VatAsync();
        Assert.IsTrue(vat == 15m,
            "НДС по исторической ставке 15%, а не по нынешней 20%: ожидалось 15, факт {0}", vat);
    }
}
