using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей (PurchaseOrder, TaxCalculation, TaxCode,
// Currency…). Тестовые скрипты НЕ получают это пространство имён глобальным
// using — без него `Currency` цепляется за посторонний недоступный тип, и ошибка
// компилятора описывает не ту причину.
using ZuloOne.Runtime.Generated;

// Оприходование заказа порождает расчёт ВХОДНОГО налога — зеркало выходного
// у счёта продажи. Проверяем направление (INPUT, а не OUTPUT: перепутанное
// направление молча превратит возмещаемый налог в налог к уплате) и то, что
// налоговый контур остаётся необязательным.
//
// Всё — типизированными сущностями через менеджеры. Оприходование идёт
// ОБЪЯВЛЕННЫМ маршрутом Draft → Ordered → Received: документ стартует в начальном
// подтипе, поэтому таблица переходов реально применяется и прыжок
// Draft → Received движок отклоняет.
public class PurchaseInputTaxTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Supplier;
    }

    private async Task<Setup> SetupAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Saudi Riyal";
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
        legalEntity.Name = "ACME KSA";
        legalEntity.RegistrationNumber = "REG-IN-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"WH-{Db.NewId():N}"[..12]; // Db.NewId() — законный остаток: генерация id.
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
        cellType.Code = $"RCV-{Db.NewId():N}"[..12];
        cellType.Name = "Receiving";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "R-01";
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
        group.Code = $"RAW-{Db.NewId():N}"[..12];
        group.Name = "Raw material";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bolt";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item.IsRawMaterial = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Supplier = supplier.MetaId };
    }

    /// <summary>Налоговый контур: справочники, ОБА направления и код по умолчанию.
    /// <para>
    /// Окно ставки задаётся параметром. По умолчанию <paramref name="rateTo"/> НЕ
    /// заполняется: окно открыто справа, поле необязательное, генерируется как
    /// DateTime? и уходит в базу NULL. Окна налога и кода всегда открыты —
    /// предметом проверки здесь является ставка.
    /// </para></summary>
    private async Task<Guid> ConfigureTaxAsync(DateTime? rateTo = null)
    {
        var from = new DateTime(2020, 1, 1);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"ZAT-{Db.NewId():N}"[..10];
        authority.Name = "ZATCA";
        authority.CountryCode = "SA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var taxType = DictionaryManager.NewRecord<TaxType>();
        taxType.Code = $"VAT-{Db.NewId():N}"[..10];
        taxType.Name = "Value added tax";
        taxType.Category = "VAT";
        taxType = await DictionaryManager.SaveRecordAsync(taxType);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"SA-{Db.NewId():N}"[..10];
        jurisdiction.Name = "Saudi Arabia";
        jurisdiction.CountryCode = "SA";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"VT-{Db.NewId():N}"[..10];
        tax.Name = "Saudi VAT";
        tax.TaxType = taxType.MetaId;
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = from;
        tax = await DictionaryManager.SaveRecordAsync(tax);

        var rate = DictionaryManager.NewRecord<TaxRate>();
        rate.Tax = tax.MetaId;
        rate.Code = $"R-{Db.NewId():N}"[..10];
        rate.Rate = 0.15m;
        rate.EffectiveFrom = from;
        rate.EffectiveTo = rateTo;
        rate = await DictionaryManager.SaveRecordAsync(rate);

        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = $"STD-{Db.NewId():N}"[..10];
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"IN-{Db.NewId():N}"[..10];
        code.Name = "Standard 15%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        code = await DictionaryManager.SaveRecordAsync(code);

        var input = DictionaryManager.NewRecord<TaxDirection>();
        input.Code = "INPUT";
        input.Name = "Input";
        input = await DictionaryManager.SaveRecordAsync(input);

        var output = DictionaryManager.NewRecord<TaxDirection>();
        output.Code = "OUTPUT";
        output.Name = "Output";
        output = await DictionaryManager.SaveRecordAsync(output);

        var settings = DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        settings = await DictionaryManager.SaveRecordAsync(settings);

        return input.MetaId;
    }

    // Подтип не передаём: NewDocumentAsync подставит НАЧАЛЬНЫЙ (Draft).
    private async Task<PurchaseOrder> NewOrderAsync(Setup s, decimal qty, decimal price)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    /// <summary>Заказ идёт объявленным маршрутом: Draft → Ordered → Received.</summary>
    private async Task ReceiveAsync(PurchaseOrder order)
    {
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
    }

    [IntegrationTest("Оприходование порождает расчёт входного налога")]
    public async Task ReceiptCreatesInputTax()
    {
        var s = await SetupAsync();
        var input = await ConfigureTaxAsync();

        // 10 × 3 = 30 базы, ставка 15% → налог 4.5.
        var order = await NewOrderAsync(s, qty: 10m, price: 3m);

        // Состояние ДО оприходования: налоговый расчёт порождает именно приход.
        Assert.IsTrue((await DocumentManager.CountDocumentsAsync<TaxCalculation>()) == 0,
            "до прихода налоговых расчётов быть не должно");

        await ReceiveAsync(order);

        var calcs = await DocumentManager.QueryDocumentsAsync<TaxCalculation>();
        Assert.IsTrue(calcs.Count == 1, "приход должен породить один расчёт налога, факт {0}", calcs.Count);

        // Строки берём вместе с документом: у менеджера список отдаёт только шапки.
        var calc = await DocumentManager.GetDocumentAsync<TaxCalculation>(calcs[0].MetaId);
        Assert.IsNotNull(calc, "расчёт налога читается");
        Assert.IsTrue(calc!.Lines.Count == 1, "одна строка налога, факт {0}", calc.Lines.Count);
        Assert.IsTrue(calc.Lines[0].TaxBase == 30m, "база = 10 × 3 = 30, факт {0}", calc.Lines[0].TaxBase);
        Assert.IsTrue(calc.Lines[0].TaxAmount == 4.5m, "налог = 30 × 15% = 4.5, факт {0}", calc.Lines[0].TaxAmount);

        // Направление именно ВХОДНОЕ: перепутанное молча превратит возмещаемый
        // налог в налог к уплате, и декларация сойдётся с обратным знаком.
        Assert.IsTrue(calc.Lines[0].Direction == input, "направление расчёта должно быть INPUT");

        var family = await DocumentManager.GetDocumentFamilyAsync(order.MetaId);
        Assert.IsTrue(family.Edges.Count > 0, "расчёт налога связан с заказом");
    }

    [IntegrationTest("Без настроенного налога приход проводится как раньше")]
    public async Task NoTaxConfigStillReceives()
    {
        var s = await SetupAsync();
        // ConfigureTaxAsync НЕ вызываем: кода налога по умолчанию нет.

        var order = await NewOrderAsync(s, qty: 4m, price: 5m);
        Assert.IsTrue(await StockAsync(s.Location, s.Item) == 0m, "черновик заказа склад не двигает");

        await ReceiveAsync(order);

        var stored = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsTrue(stored?.Subtype == PurchaseOrder.Subtypes.Received,
            "приход проведён несмотря на ненастроенный налог, факт {0}", stored?.Subtype);
        Assert.IsTrue((await DocumentManager.CountDocumentsAsync<TaxCalculation>()) == 0, "расчёт налога не создан");

        // И сам приход при этом отработал полностью.
        var stock = await StockAsync(s.Location, s.Item);
        Assert.IsTrue(stock == 4m, "остаток ячейки 4, факт {0}", stock);
    }

    [IntegrationTest("Истёкшая на дату прихода ставка не даёт оприходовать заказ")]
    public async Task ExpiredRateBlocksReceipt()
    {
        var s = await SetupAsync();
        // Контур НАСТРОЕН, но ставка закрыта в 2020 году, а приход датируется сегодня.
        // Зеркало проверки у счёта продажи: возмещаемый входной налог не должен
        // пропадать молча (ср. NoTaxConfigStillReceives — там налогов просто нет).
        await ConfigureTaxAsync(rateTo: new DateTime(2020, 12, 31));

        var order = await NewOrderAsync(s, qty: 10m, price: 3m);

        // Отказ приходит ИСКЛЮЧЕНИЕМ, а бросок обрекает окружающую транзакцию
        // прогона — после catch к базе не обращаемся. Причина проверяется НАРОЧНО:
        // «что-то бросило» прошло бы и от любой другой проверки прихода.
        var reason = "";
        try
        {
            await ReceiveAsync(order);
        }
        catch (Exception ex)
        {
            for (Exception? e = ex; e is not null; e = e.InnerException) reason += e.Message + " | ";
        }

        Assert.IsTrue(reason.Length > 0, "приход без действующей на его дату ставки должен быть отклонён");
        Assert.IsTrue(reason.Contains("действующей ставки"),
            "отказ должен быть именно про отсутствие действующей ставки, факт: {0}", reason);
    }

    // Срез регистра адресуется измерениями, а не SQL-строкой.
    private Task<decimal> StockAsync(Guid location, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = location, ["Item"] = item });
}
