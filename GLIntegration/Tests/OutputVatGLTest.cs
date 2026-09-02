using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей. Тест-скриптам этот namespace НЕ приходит
// глобальным using'ом.
using ZuloOne.Runtime.Generated;

// НАЧИСЛЕННЫЙ НДС ОБЯЗАН СТАТЬ ОБЯЗАТЕЛЬСТВОМ В КНИГЕ.
//
// Налог считался и попадал в свои регистры, но обязательством в главной книге не
// становился никогда: счёт продажи разносил Dr дебиторка / Cr выручка на сумму
// БЕЗ налога, и на этом всё заканчивалось. Книга не знала, что компания должна
// государству.
//
// Проверка идёт ПО СЧЕТАМ — через строки JournalEntry, порождённые расчётом
// налога. Сумма по всей книге сошлась бы всегда (инвариант двойной записи) и
// прошла бы даже при разноске не на те счета.
public class OutputVatGLTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Cell;
        public Guid Item;
        public Guid Customer;
        public Guid ArAccount;
        public Guid VatAccount;
        public Guid CashAccount;
        public Guid LegalEntity;
    }

    private async Task<Setup> SetupAsync(bool configureVatAccount = true)
    {
        var today = DateTime.UtcNow.Date;

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
        legalEntity.RegistrationNumber = $"REG-VAT-{Db.NewId():N}"[..16];
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

        await TotalsManager.PostMovementAsync("Stock", null, today,
            new Dictionary<string, object?> { ["Cell"] = cell.MetaId, ["Item"] = item.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 100m });

        var arAccount = await NewAccountAsync("1200", "Accounts receivable", AccountType.Asset, currency.MetaId);
        var vatAccount = await NewAccountAsync("2300", "VAT payable", AccountType.Liability, currency.MetaId);
        await NewAccountAsync("4000", "Revenue", AccountType.Income, currency.MetaId);
        var cashAccount = await NewAccountAsync("1000", "Cash", AccountType.Asset, currency.MetaId);

        // Настройки — ОДИНОЧНЫЙ и КЭШИРУЕМЫЙ справочник: правим существующую
        // запись, если она есть, иначе заводим. Слепой NewRecord делает тест
        // зависимым от порядка — кэш переживает откат транзакции кейса, и запись
        // соседнего прогона может подменить нашу.
        var accRows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        var settings = accRows.Count > 0 ? accRows[0] : DictionaryManager.NewRecord<AccountingSettings>();
        settings.ArAccountCode = "1200";
        settings.RevenueAccountCode = "4000";
        settings.VatPayableAccountCode = configureVatAccount ? "2300" : null;
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

        await ConfigureTaxAsync();

        return new Setup
        {
            Cell = cell.MetaId,
            Item = item.MetaId,
            Customer = customer.MetaId,
            ArAccount = arAccount,
            VatAccount = vatAccount,
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

    /// <summary>Налоговый контур: 15% исходящего НДС кодом по умолчанию.</summary>
    private async Task ConfigureTaxAsync()
    {
        var from = new DateTime(2020, 1, 1);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"ZAT-{Db.NewId():N}"[..10];
        authority.Name = "ZATCA";
        authority.CountryCode = "SA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"SA-{Db.NewId():N}"[..10];
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

        var taxRows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var settings = taxRows.Count > 0 ? taxRows[0] : DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    private static async Task<SalesInvoice> IssueAsync(Setup s, decimal quantity, decimal unitPrice)
    {
        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = s.Customer;
        invoice.Location = s.Cell;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = quantity, UnitPrice = unitPrice });
        await DocumentManager.SaveDocumentAsync(invoice);

        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
        return invoice;
    }

    /// <summary>Единственный расчёт налога, порождённый выставлением счёта.</summary>
    private static async Task<TaxCalculation> TheCalculationAsync()
    {
        var all = await DocumentManager.QueryDocumentsAsync<TaxCalculation>();
        Assert.IsTrue(all.Count == 1, "должен появиться один расчёт налога, факт {0}", all.Count);
        return (await DocumentManager.GetDocumentAsync<TaxCalculation>(all[0].MetaId))!;
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

    [IntegrationTest("Исходящий НДС кредитует счёт НДС и доводит дебиторку до суммы с налогом")]
    public async Task OutputVatBecomesLiability()
    {
        var s = await SetupAsync();

        // 4 × 25 = 100 базы, ставка 15% → налог 15.
        var invoice = await IssueAsync(s, 4m, 25m);
        var calc = await TheCalculationAsync();

        var vat = await AccountAsync(calc.MetaId, s.VatAccount);
        Assert.IsTrue(vat.Credit == 15m,
            "налог 15 обязан стать обязательством, факт {0}", vat.Credit);

        // Дебиторка добирается до суммы С налогом: счёт продажи дебетовал 100
        // (без налога), расчёт добавляет недостающие 15 → 115 всего.
        var arFromInvoice = await AccountAsync(invoice.MetaId, s.ArAccount);
        var arFromTax = await AccountAsync(calc.MetaId, s.ArAccount);
        Assert.IsTrue(arFromInvoice.Debit == 100m,
            "счёт продажи дебетует дебиторку на 100 без налога, факт {0}", arFromInvoice.Debit);
        Assert.IsTrue(arFromTax.Debit == 15m,
            "расчёт налога добирает недостающие 15, факт {0}", arFromTax.Debit);
        Assert.IsTrue(arFromInvoice.Debit + arFromTax.Debit == 115m,
            "итого дебиторка 115 — сумма, которую реально должен покупатель, факт {0}",
            arFromInvoice.Debit + arFromTax.Debit);
    }

    [IntegrationTest("Без настроенного счёта НДС счёт продажи выставляется как прежде")]
    public async Task UnconfiguredVatAccountDoesNotBreakIssue()
    {
        // Разноска best-effort: ненастроенная бухгалтерия не должна мешать продавать.
        var s = await SetupAsync(configureVatAccount: false);

        var invoice = await IssueAsync(s, 4m, 25m);
        var calc = await TheCalculationAsync();

        var stored = await DocumentManager.GetDocumentAsync<SalesInvoice>(invoice.MetaId);
        Assert.IsTrue(stored?.Subtype == SalesInvoice.Subtypes.Issued,
            "счёт выставлен несмотря на ненастроенный счёт НДС, факт {0}", stored?.Subtype);

        var vat = await AccountAsync(calc.MetaId, s.VatAccount);
        Assert.IsTrue(vat.Credit == 0m, "проводки по НДС нет, факт {0}", vat.Credit);

        // И налог сам по себе посчитан — он живёт в своём регистре независимо от книги.
        decimal ledger = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("TaxLedger"))
            ledger += Convert.ToDecimal(r["TaxAmount"]);
        Assert.IsTrue(ledger == 15m, "налог начислен в TaxLedger, факт {0}", ledger);
    }

    [IntegrationTest("Оплата покупателя кредитует дебиторку — счёт в книге закрывается вместе с налогом")]
    public async Task CustomerPaymentClosesReceivableInLedger()
    {
        // САМАЯ ДОРОГАЯ ПОЛОВИНА РАСХОЖДЕНИЯ. Счёт дебетует дебиторку на сумму без
        // налога, проводка НДС добавляет к ней налог — а кредитовать её было нечем.
        // Счёт дебиторки в книге рос на всю выручку с налогом за историю, тогда как
        // регистр Receivable гасился оплатой.
        //
        // 4 × 25 = 100 базы + 15% = 15 налога → покупатель должен 115, и ровно 115
        // закрывают счёт в книге.
        var s = await SetupAsync();

        var invoice = await IssueAsync(s, 4m, 25m);
        var calc = await TheCalculationAsync();

        var fromInvoice = await AccountAsync(invoice.MetaId, s.ArAccount);
        var fromTax = await AccountAsync(calc.MetaId, s.ArAccount);
        Assert.IsTrue(fromInvoice.Debit + fromTax.Debit == 115m,
            "дебиторка в книге 100 + 15 = 115, факт {0}", fromInvoice.Debit + fromTax.Debit);

        var payment = await DocumentManager.NewDocumentAsync<CustomerPayment>();
        payment.LegalEntity = s.LegalEntity;
        payment.Lines.Add(new CustomerPaymentLinesTablePartRow { Customer = s.Customer, Amount = 115m });
        await DocumentManager.SaveDocumentAsync(payment);

        Assert.IsTrue((await AccountAsync(payment.MetaId, s.ArAccount)).Credit == 0m,
            "черновик оплаты не кредитует дебиторку");

        payment.Subtype = CustomerPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        var paid = await AccountAsync(payment.MetaId, s.ArAccount);
        Assert.IsTrue(paid.Credit == 115m,
            "оплата кредитует дебиторку на 115, факт {0}", paid.Credit);

        var cash = await AccountAsync(payment.MetaId, s.CashAccount);
        Assert.IsTrue(cash.Debit == 115m,
            "и дебетует денежные средства на ту же сумму, факт {0}", cash.Debit);

        Assert.IsTrue(fromInvoice.Debit + fromTax.Debit - paid.Credit == 0m,
            "счёт дебиторки закрыт: 115 − 115 = 0, факт {0}",
            fromInvoice.Debit + fromTax.Debit - paid.Credit);
    }

    [IntegrationTest("Оплата покупателя с неположительной суммой отклоняется")]
    public async Task NonPositiveCustomerPaymentRejected()
    {
        // Без этой проверки оплата с ОТРИЦАТЕЛЬНОЙ суммой проводилась и НАРАЩИВАЛА
        // долг вместо погашения: Receivable заведён с allowNegativeBalance=true,
        // движок такое не ловит. Зеркало проверки у оплаты поставщику.
        var s = await SetupAsync();

        var payment = await DocumentManager.NewDocumentAsync<CustomerPayment>();
        payment.LegalEntity = s.LegalEntity;
        payment.Lines.Add(new CustomerPaymentLinesTablePartRow { Customer = s.Customer, Amount = -500m });
        await DocumentManager.SaveDocumentAsync(payment);

        var reason = string.Empty;
        try
        {
            payment.Subtype = CustomerPayment.Subtypes.Paid;
            await DocumentManager.SaveDocumentAsync(payment);
        }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Contains("больше нуля"),
            "отрицательная оплата обязана быть отклонена с внятной причиной, факт: {0}", reason);
    }
}
