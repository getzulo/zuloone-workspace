using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Оплата налога гасит обязательство В КНИГЕ, а не в регистрах.
//
// TaxLedger хранит начисление (это факт декларации) и оплатой не сторнируется.
// Регистра TaxPayable нет — заводить его здесь нельзя. Payable (кредиторка
// поставщику) к налогу не относится и обязан остаться нетронутым: иначе
// платёж в бюджет молча закрыл бы чужой долг.
public class TaxPaymentTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private sealed class Setup
    {
        public Guid LegalEntity;
        public Guid TaxCode;
        public Guid VatAccount;
        public Guid CashAccount;
    }

    private async Task<Setup> SetupAsync()
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
        legalEntity.RegistrationNumber = $"REG-TP-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var vatAccount = await NewAccountAsync("2300", "VAT payable", AccountType.Liability, currency.MetaId);
        var cashAccount = await NewAccountAsync("1000", "Cash", AccountType.Asset, currency.MetaId);

        // Настройки — одиночный кэшируемый справочник: правим существующую
        // запись, иначе кэш соседнего прогона подменит счета.
        var accRows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        var settings = accRows.Count > 0 ? accRows[0] : DictionaryManager.NewRecord<AccountingSettings>();
        settings.VatPayableAccountCode = "2300";
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

        var taxCode = await NewTaxCodeAsync();

        return new Setup
        {
            LegalEntity = legalEntity.MetaId,
            TaxCode = taxCode,
            VatAccount = vatAccount,
            CashAccount = cashAccount,
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

    private async Task<Guid> NewTaxCodeAsync()
    {
        var uniq = $"{Db.NewId():N}"[..8];
        var from = new DateTime(2020, 1, 1);

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"T-{uniq}";
        tax.Name = "VAT";
        tax.Authority = Db.NewId();
        tax.Jurisdiction = Db.NewId();
        tax.EffectiveFrom = from;
        tax = await DictionaryManager.SaveRecordAsync(tax);

        var rate = DictionaryManager.NewRecord<TaxRate>();
        rate.Tax = tax.MetaId;
        rate.Code = $"R-{uniq}";
        rate.Rate = 0.15m;
        rate.EffectiveFrom = from;
        rate = await DictionaryManager.SaveRecordAsync(rate);

        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = $"STD-{uniq}";
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"C-{uniq}";
        code.Name = "Standard 15%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        return (await DictionaryManager.SaveRecordAsync(code)).MetaId;
    }

    /// <summary>
    /// Дебет/кредит одного счёта по JournalEntry, порождённым документом.
    /// Разрез по счёту из регистра GL не достать — у него нет физических измерений.
    /// </summary>
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

    private async Task<int> PayableMovementsAsync()
        => (await Db.QueryMovementsAsync("Payable")).Count;

    [IntegrationTest("Paid с юрлицом пишет JE Dr НДС к уплате / Cr деньги; Payable не тронут")]
    public async Task PaidWithLegalEntityPostsJournal()
    {
        var s = await SetupAsync();
        var payableBefore = await PayableMovementsAsync();

        var payment = await DocumentManager.NewDocumentAsync<TaxPayment>();
        payment.LegalEntity = s.LegalEntity;
        payment.Lines.Add(new TaxPaymentLinesTablePartRow { TaxCode = s.TaxCode, Amount = 150m });
        await DocumentManager.SaveDocumentAsync(payment);

        payment.Subtype = TaxPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        var vat = await AccountAsync(payment.MetaId, s.VatAccount);
        Assert.IsTrue(vat.Debit == 150m,
            "оплата дебетует НДС к уплате на 150, факт {0}", vat.Debit);

        var cash = await AccountAsync(payment.MetaId, s.CashAccount);
        Assert.IsTrue(cash.Credit == 150m,
            "и кредитует денежные средства на ту же сумму, факт {0}", cash.Credit);

        Assert.IsTrue(await PayableMovementsAsync() == payableBefore,
            "оплата налога не должна писать в Payable, факт {0}", await PayableMovementsAsync());
    }

    [IntegrationTest("Черновик оплаты налога книгу не трогает")]
    public async Task DraftDoesNotPostGl()
    {
        var s = await SetupAsync();

        var payment = await DocumentManager.NewDocumentAsync<TaxPayment>();
        payment.LegalEntity = s.LegalEntity;
        payment.Lines.Add(new TaxPaymentLinesTablePartRow { TaxCode = s.TaxCode, Amount = 150m });
        await DocumentManager.SaveDocumentAsync(payment);

        Assert.IsTrue((await AccountAsync(payment.MetaId, s.VatAccount)).Debit == 0m,
            "черновик не дебетует НДС к уплате");
        Assert.IsTrue((await AccountAsync(payment.MetaId, s.CashAccount)).Credit == 0m,
            "черновик не кредитует денежные средства");
    }

    [IntegrationTest("Paid без юрлица проводится, проводки нет")]
    public async Task PaidWithoutLegalEntitySkipsLedger()
    {
        var s = await SetupAsync();

        var payment = await DocumentManager.NewDocumentAsync<TaxPayment>();
        payment.Lines.Add(new TaxPaymentLinesTablePartRow { TaxCode = s.TaxCode, Amount = 150m });
        await DocumentManager.SaveDocumentAsync(payment);
        payment.Subtype = TaxPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        var stored = await DocumentManager.GetDocumentAsync<TaxPayment>(payment.MetaId);
        Assert.IsTrue(stored?.Subtype == TaxPayment.Subtypes.Paid,
            "платёж проведён, факт {0}", stored?.Subtype);
        Assert.IsTrue((await AccountAsync(payment.MetaId, s.VatAccount)).Debit == 0m,
            "без юрлица проводки нет");
    }
}
