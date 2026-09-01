using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (Currency, SalesInvoice, TaxCalculation, …TablePartRow).
// Тест-скрипты НЕ получают это пространство имён глобальным using — без него
// генерённые классы не находятся, а Currency вдобавок связывается с посторонним
// недоступным типом, и ошибка (CS0122) описывает не ту причину.
using ZuloOne.Runtime.Generated;

// Выставление счёта порождает расчёт ВЫХОДНОГО налога отдельным документом,
// связанным со счётом. Проверяем и сам факт порождения, и то, что налоговый
// контур необязателен: без кода налога по умолчанию счёт выставляется как
// раньше — иначе включение налогов сломало бы все существующие продажи.
//
// Всё написано типизированными сущностями через менеджеры — той же дверью, что
// и обработчик SalesInvoiceEventHandler, который здесь и проверяется:
// справочник — NewRecord<T> → поля → SaveRecordAsync, счёт —
// NewDocumentAsync<T> → Lines → SaveDocumentAsync, выставление — присваивание
// подтипа плюс save (MIQS doc.SubtypeID = …; SaveDocument(doc)).
public class SalesOutputTaxTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private async Task<(Guid Cell, Guid Item, Guid Customer, Guid Currency)> SetupAsync()
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
        legalEntity.RegistrationNumber = "REG-OUT-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"SP-{Db.NewId():N}"[..12];
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

        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = cell.MetaId, ["Item"] = item.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 100m });

        return (cell.MetaId, item.MetaId, customer.MetaId, currency.MetaId);
    }

    /// <summary>Налоговый контур: справочники + код налога по умолчанию в настройках.
    /// <para>
    /// Окно ставки задаётся параметрами. По умолчанию <paramref name="rateTo"/> НЕ
    /// заполняется: окно открыто справа, поле необязательное, генерируется как
    /// DateTime? и уходит в базу NULL — типизированное сохранение справляется само.
    /// Окна самого налога и кода всегда открыты: предметом проверки здесь является
    /// ставка.
    /// </para></summary>
    private async Task ConfigureTaxAsync(DateTime? rateTo = null)
    {
        var from = new DateTime(2020, 1, 1);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"ZAT-{Db.NewId():N}"[..10];
        authority.Name = "ZATCA";
        authority.CountryCode = "SA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var type = DictionaryManager.NewRecord<TaxType>();
        type.Code = $"VAT-{Db.NewId():N}"[..10];
        type.Name = "Value added tax";
        type.Category = "VAT";
        type = await DictionaryManager.SaveRecordAsync(type);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"SA-{Db.NewId():N}"[..10];
        jurisdiction.Name = "Saudi Arabia";
        jurisdiction.CountryCode = "SA";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"VT-{Db.NewId():N}"[..10];
        tax.Name = "Saudi VAT";
        tax.TaxType = type.MetaId;
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

        var settings = DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    /// <summary>Черновик счёта на одну строку — ещё НЕ выставлен.</summary>
    private static async Task<SalesInvoice> NewInvoiceAsync(Guid customer, Guid cell, Guid item, decimal quantity, decimal unitPrice)
    {
        // Подтип не передаём: NewDocumentAsync обязан взять НАЧАЛЬНЫЙ подтип типа
        // документа (Draft) сам.
        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = customer;
        invoice.Location = cell;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = item, Quantity = quantity, UnitPrice = unitPrice });
        await DocumentManager.SaveDocumentAsync(invoice);
        return invoice;
    }

    [IntegrationTest("Выставление счёта порождает расчёт выходного налога")]
    public async Task IssueCreatesOutputTax()
    {
        var s = await SetupAsync();
        await ConfigureTaxAsync();

        // 4 × 25 = 100 базы, ставка 15% → налог 15.
        var inv = await NewInvoiceAsync(s.Customer, s.Cell, s.Item, 4m, 25m);

        // Состояние ДО перехода: расчёт налога рождает именно ВЫСТАВЛЕНИЕ.
        // Без этой проверки «один расчёт» ниже проходит и тогда, когда счёт
        // породил его ещё при сохранении черновика.
        Assert.IsTrue((await DocumentManager.QueryDocumentsAsync<TaxCalculation>()).Count == 0,
            "черновик счёта не должен порождать расчёт налога");

        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);

        var calcs = await DocumentManager.QueryDocumentsAsync<TaxCalculation>();
        Assert.IsTrue(calcs.Count == 1, "счёт должен породить один расчёт налога, факт {0}", calcs.Count);

        var calc = await DocumentManager.GetDocumentAsync<TaxCalculation>(calcs[0].MetaId);
        Assert.IsNotNull(calc, "расчёт налога читается как генерённый класс");
        Assert.IsTrue(calc!.Lines.Count == 1, "одна строка налога, факт {0}", calc.Lines.Count);
        Assert.IsTrue(calc.Lines[0].TaxBase == 100m,
            "база = 4 × 25 = 100, факт {0}", calc.Lines[0].TaxBase);
        Assert.IsTrue(calc.Lines[0].TaxAmount == 15m,
            "налог = 100 × 15% = 15, факт {0}", calc.Lines[0].TaxAmount);

        // Расчёт связан со счётом — родословная документов, а не поле-указатель.
        var family = await DocumentManager.GetDocumentFamilyAsync(inv.MetaId);
        Assert.IsTrue(family.Edges.Count > 0, "расчёт налога связан со счётом");
        Assert.IsTrue(family.Edges.Any(e => e.ParentDocId == inv.MetaId && e.ChildDocId == calc.MetaId),
            "ребро ведёт от счёта к расчёту налога");
    }

    [IntegrationTest("Товар со слоями себестоимости порождает РОВНО ОДИН расчёт налога")]
    public async Task CostLayersDoNotDuplicateOutputTax()
    {
        var s = await SetupAsync();
        await ConfigureTaxAsync();

        // ЕДИНСТВЕННОЕ отличие от IssueCreatesOutputTax — у товара есть партия
        // себестоимости. Значит проведение счёта запустит драйвер CostingIssue, и
        // тот допишет документу ВТОРИЧНЫЕ движения (списание себестоимости).
        //
        // Именно на таких данных вылезло удвоение разноски в GL: побочный эффект
        // after-post исполнялся дважды. Тест закрывает вопрос, тянется ли то же
        // удвоение на порождение налогового расчёта, — а заодно охраняет от него
        // впредь. Обычные тесты этого не видят: они заводят остаток прямым
        // движением регистра, списывать нечего, вторичных движений нет.
        await TotalsManager.PostMovementAsync("ItemCostFifo", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Quantity"] = 100m, ["Amount"] = 700m });

        var inv = await NewInvoiceAsync(s.Customer, s.Cell, s.Item, 4m, 25m);
        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);

        var calcs = await DocumentManager.QueryDocumentsAsync<TaxCalculation>();
        Assert.IsTrue(calcs.Count == 1,
            "расчёт налога один, сколько бы раз ни сработало after-post, факт {0}", calcs.Count);
    }

    [IntegrationTest("Без настроенного налога счёт выставляется как раньше")]
    public async Task NoTaxConfigStillIssues()
    {
        var s = await SetupAsync();
        // ConfigureTaxAsync НЕ вызываем: кода налога по умолчанию нет.

        var inv = await NewInvoiceAsync(s.Customer, s.Cell, s.Item, 2m, 10m);
        Assert.IsTrue(inv.Subtype == SalesInvoice.Subtypes.Draft,
            "новый счёт стартует в начальном подтипе Draft, факт {0}", inv.Subtype);

        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);

        var stored = await DocumentManager.GetDocumentAsync<SalesInvoice>(inv.MetaId);
        Assert.IsTrue(stored?.Subtype == SalesInvoice.Subtypes.Issued,
            "счёт выставлен несмотря на ненастроенный налог, факт {0}", stored?.Subtype);
        var calcs = await DocumentManager.QueryDocumentsAsync<TaxCalculation>();
        Assert.IsTrue(calcs.Count == 0, "расчёт налога не создан, факт {0}", calcs.Count);
    }

    [IntegrationTest("Истёкшая на дату счёта ставка не даёт выставить счёт")]
    public async Task ExpiredRateBlocksIssue()
    {
        var s = await SetupAsync();
        // Контур НАСТРОЕН, но ставка закрыта в 2020 году, а счёт датируется сегодня.
        // Это не «налоги выключены» (см. NoTaxConfigStillIssues) — это порванная
        // настройка, и счёт без НДС уходить клиенту не должен.
        await ConfigureTaxAsync(rateTo: new DateTime(2020, 12, 31));

        var inv = await NewInvoiceAsync(s.Customer, s.Cell, s.Item, 4m, 25m);

        // Отказ приходит ИСКЛЮЧЕНИЕМ, а бросок происходит внутри окружающей
        // транзакции прогона и обрекает её. Поэтому после catch к базе больше не
        // обращаемся — утверждение делается о самом отказе и его причине.
        // Причина проверяется НАРОЧНО: «что-то бросило» прошло бы и от нехватки
        // остатка, то есть не доказывало бы ничего про налог.
        var reason = "";
        try
        {
            inv.Subtype = SalesInvoice.Subtypes.Issued;
            await DocumentManager.SaveDocumentAsync(inv);
        }
        catch (Exception ex)
        {
            for (Exception? e = ex; e is not null; e = e.InnerException) reason += e.Message + " | ";
        }

        Assert.IsTrue(reason.Length > 0, "счёт без действующей на его дату ставки должен быть отклонён при выставлении");
        Assert.IsTrue(reason.Contains("действующей ставки"),
            "отказ должен быть именно про отсутствие действующей ставки, факт: {0}", reason);
    }
}
