using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// The generated entity classes (JournalEntry, Currency, JournalEntryLinesTablePartRow…).
// Test scripts do NOT get this namespace as a global using, so it must be named — without
// it `Currency` binds to an inaccessible type elsewhere and the rest are simply not found.
using ZuloOne.Runtime.Generated;

// Integration coverage for the Accounting foundation: a balanced journal entry posts
// to the general ledger, and the double-entry guard rejects an unbalanced one.
//
// Written the way a MIQS service is written — typed entities through the managers.
// A record is NewRecord<T> → fill → SaveRecordAsync; a document is NewDocumentAsync<T>
// → fill Lines → SaveDocumentAsync; and posting is an assignment:
//
//     entry.Subtype = JournalEntry.Subtypes.Posted;
//     await DocumentManager.SaveDocumentAsync(entry);
//
// exactly as MIQS writes `doc.SubtypeID = …; DocumentManager.SaveDocument(doc)`.
// SaveDocumentAsync routes the changed subtype through the posting engine, so the
// test drives the same door production code does — no name strings, no data-shaped
// facade.
public class AccountingPostingTest : IntegrationTestScriptBase
{
    // The managers, as ambient properties (MIQS has these on the script base; the
    // platform's Runtime assembly cannot name Core's interfaces, so they live here).
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    // Shared master data for a posting scenario (each case runs in its own rolled-back
    // transaction, so every case builds its own).
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
        legalEntity.RegistrationNumber = "REG-ACC-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var fiscalYear = DictionaryManager.NewRecord<FiscalYear>();
        fiscalYear.Code = "FY2026";
        fiscalYear.StartDate = new DateTime(2026, 1, 1);
        fiscalYear.EndDate = new DateTime(2026, 12, 31);
        fiscalYear.IsClosed = false;
        fiscalYear = await DictionaryManager.SaveRecordAsync(fiscalYear);

        var period = DictionaryManager.NewRecord<FiscalPeriod>();
        period.FiscalYear = fiscalYear.MetaId;
        period.Code = "2026-08";
        period.FromDate = new DateTime(2026, 8, 1);
        period.ToDate = new DateTime(2026, 8, 31);
        period.Status = "Open";
        period = await DictionaryManager.SaveRecordAsync(period);

        var cash = DictionaryManager.NewRecord<ChartOfAccounts>();
        cash.Code = "1000";
        cash.Name = "Cash";
        cash.AccountType = AccountType.Asset;
        cash.IsPostable = true;
        cash = await DictionaryManager.SaveRecordAsync(cash);

        var revenue = DictionaryManager.NewRecord<ChartOfAccounts>();
        revenue.Code = "4000";
        revenue.Name = "Revenue";
        revenue.AccountType = AccountType.Income;
        revenue.IsPostable = true;
        revenue = await DictionaryManager.SaveRecordAsync(revenue);

        return new Setup
        {
            LegalEntity = legalEntity.MetaId,
            Currency = currency.MetaId,
            FiscalPeriod = period.MetaId,
            Cash = cash.MetaId,
            Revenue = revenue.MetaId,
        };
    }

    private sealed class Setup
    {
        public Guid LegalEntity;
        public Guid Currency;
        public Guid FiscalPeriod;
        public Guid Cash;
        public Guid Revenue;
    }

    // A Draft entry with two lines. No subtype is passed on purpose: NewDocumentAsync
    // must fall back to the type's INITIAL subtype (Draft). It used to fall back to
    // NULL, and a null subtype is not neutral — BuildChainAsync scopes the posting
    // chain by subtype, so NULL narrowed to nothing, picked up the Posted chain, and
    // postOnSave posted the "draft" on the spot. The empty-ledger assertions below are
    // what catch that.
    private async Task<JournalEntry> NewEntryAsync(Setup s, decimal debit, decimal credit, string description)
    {
        var entry = await DocumentManager.NewDocumentAsync<JournalEntry>();
        entry.LegalEntity = s.LegalEntity;
        entry.Currency = s.Currency;
        entry.FiscalPeriod = s.FiscalPeriod;
        entry.Description = description;

        entry.Lines.Add(new JournalEntryLinesTablePartRow { Account = s.Cash, Debit = debit, Credit = 0m });
        entry.Lines.Add(new JournalEntryLinesTablePartRow { Account = s.Revenue, Debit = 0m, Credit = credit });

        await DocumentManager.SaveDocumentAsync(entry);
        return entry;
    }

    [IntegrationTest("Сбалансированная проводка разносится в GL")]
    public async Task BalancedEntryPostsToLedger()
    {
        var s = await SetupAsync();
        var entry = await NewEntryAsync(s, debit: 100m, credit: 100m, "Balanced");

        // A Draft carries no movements. Assert it BEFORE posting — without this the
        // assertions below pass even when the document posted itself on save, and the
        // test proves nothing about the transition.
        Assert.IsTrue((await TotalsManager.QueryMovementsAsync("GL")).Count == 0,
            "черновик не должен порождать движений GL");

        // Posting is the Draft → Posted transition, and the transition is an
        // assignment plus a save.
        entry.Subtype = JournalEntry.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(entry);

        var movements = await TotalsManager.QueryMovementsAsync("GL");
        Assert.IsTrue(movements.Count == 2, "ожидалось 2 движения GL (по строке на счёт), а не {0}", movements.Count);

        decimal debit = 0m, credit = 0m;
        foreach (var m in movements)
        {
            debit += Convert.ToDecimal(m["Debit"]);
            credit += Convert.ToDecimal(m["Credit"]);
        }
        Assert.IsTrue(debit == 100m, "сумма дебета GL должна быть 100, а не {0}", debit);
        Assert.IsTrue(credit == 100m, "сумма кредита GL должна быть 100, а не {0}", credit);
    }

    [IntegrationTest("Откат из Posted снимает движения GL")]
    public async Task UnpostReversesLedger()
    {
        var s = await SetupAsync();
        var entry = await NewEntryAsync(s, debit: 100m, credit: 100m, "Balanced");

        entry.Subtype = JournalEntry.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(entry);
        Assert.IsTrue((await TotalsManager.QueryMovementsAsync("GL")).Count == 2,
            "проводка должна была разнестись перед откатом");

        entry.Subtype = JournalEntry.Subtypes.Draft;
        await DocumentManager.SaveDocumentAsync(entry);
        Assert.IsTrue((await TotalsManager.QueryMovementsAsync("GL")).Count == 0,
            "возврат в Draft должен снять движения GL");
    }

    [IntegrationTest("Несбалансированная проводка отклоняется")]
    public async Task UnbalancedEntryIsRejected()
    {
        var s = await SetupAsync();

        // An unbalanced entry must SAVE cleanly as a Draft — a draft is allowed to be
        // wrong. The guard belongs to POSTING, so it is the Draft → Posted transition
        // that has to refuse, and a draft that saves silently into the ledger would be
        // the real defect.
        var entry = await NewEntryAsync(s, debit: 100m, credit: 60m, "Unbalanced");
        Assert.IsTrue((await TotalsManager.QueryMovementsAsync("GL")).Count == 0,
            "несбалансированный черновик не должен порождать движений GL");

        // The guard refuses by THROWING, and the throw happens inside the runner's
        // ambient transaction — which dooms it. Reading the register after the catch,
        // as this case used to, then fails with "the operation is not valid for the
        // state of the transaction". So assert on the refusal itself and touch the
        // database no further.
        var rejected = false;
        try
        {
            entry.Subtype = JournalEntry.Subtypes.Posted;
            await DocumentManager.SaveDocumentAsync(entry);
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "несбалансированная проводка (Дт 100 / Кт 60) должна быть отклонена при проведении");
    }
}
