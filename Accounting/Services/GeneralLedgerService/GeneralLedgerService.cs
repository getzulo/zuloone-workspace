using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Service "GeneralLedgerService": IGeneralLedgerService contract. The single
// point that posts subledgers (sales, purchasing, payroll) into the general
// ledger. The subsystem decides WHAT and TO WHICH accounts; all journal
// mechanics live here.
//
// Accounts are taken from the PROFILE — the singleton AccountingSettings
// dictionary (the module settings form), not from global constants: the chart
// is configured in the UI.
//
// IMPORTANT about DI: dependencies are resolved LAZILY from the scope, NOT
// through the constructor. Platform managers take context via IDbContextFactory
// — each creates ITS OWN connection, and a constructor that resolves them as a
// batch inside an already-open posting transaction forces it to promote to
// distributed (the container has no MSDTC → "Failure while attempting to
// promote transaction"). Lazy resolve repeats inline-code behaviour: one
// manager at a time.
//
// Posting is BEST-EFFORT: no accounts / period / legal entity → null, the
// caller skips quietly.
public partial class GeneralLedgerService
{
    private static readonly Guid JournalEntryType = Guid.Parse("188246b3-5ed0-4da0-98cb-a86b6da36581");

    // Managers are injected the ordinary way. Capturing IServiceProvider and
    // resolving from it lazily is FORBIDDEN: the service outlives its scope, and
    // by the after-post event (it runs after the scope is closed) such a
    // provider throws ObjectDisposedException — posting disappeared silently.
    private readonly IDictionaryManager<AccountingSettings> _settings;
    private readonly IDictionaryManager<ChartOfAccounts> _accounts;
    private readonly IDictionaryManager<FiscalPeriod> _periods;
    private readonly IDocumentManager _documents;
    private readonly IDocumentPostingService _posting;

    public GeneralLedgerService(
        IDictionaryManager<AccountingSettings> settings,
        IDictionaryManager<ChartOfAccounts> accounts,
        IDictionaryManager<FiscalPeriod> periods,
        IDocumentManager documents,
        IDocumentPostingService posting)
    {
        _settings = settings;
        _accounts = accounts;
        _periods = periods;
        _documents = documents;
        _posting = posting;
    }

    /// <summary>Accounting settings profile (one record); null if not filled yet.</summary>
    public async Task<AccountingSettings?> GetSettingsAsync()
        => (await _settings.GetRecordsAsync()).FirstOrDefault();

    /// <summary>Chart account by code; null if the code is empty or the account is not found.</summary>
    public async Task<Guid?> ResolveAccountAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var accounts = _accounts;
        return (await accounts.GetRecordsAsync($"Code = '{code}'")).FirstOrDefault()?.MetaId;
    }

    /// <summary>
    /// Why an account code is NOT fit for postings — or null if it is.
    ///
    /// WHAT IS A PROBLEM AND WHAT IS NOT. An empty code and a NON-EXISTENT
    /// account code mean the same: "this leg is not configured yet". That is a
    /// lawful state — the profile is filled before the chart is finished, and
    /// posting skips such a leg quietly, as before. Complaining about it would
    /// forbid saving the profile until every one of the twelve accounts exists.
    ///
    /// The problem is a code of an EXISTING account marked UNPOSTABLE. That is
    /// no longer "not configured" but configured WRONG: the field looks filled,
    /// the account is in the chart, and a posting to it will never happen. That
    /// is the case to catch where the person sees the field.
    ///
    /// ONE RULE FOR TWO DOORS: saving the settings profile and posting itself
    /// (ResolvePairAsync filters unpostable accounts by the same flag).
    /// Postings accept only LEAVES: a group account's own balance must be the
    /// sum of children, and a direct posting to it breaks that. Setting
    /// IsPostable on a group is blocked by ChartOfAccountsEventHandler.
    /// </summary>
    public async Task<string?> AccountCodeProblemAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var account = (await _accounts.GetRecordsAsync($"Code = '{code}'")).FirstOrDefault();
        if (account is null) return null;   // account not there yet — "not configured", not an error
        if (!account.IsPostable)
            return $"счёт «{code}» ({account.Name}) не проводимый — это группа, "
                 + "проводки принимают только конечные счета";
        return null;
    }

    /// <summary>A pair of accounts in ONE query. Each manager call is two DB
    /// hits (table metadata + data), and each takes a connection from the pool
    /// and joins the posting transaction; extra round-trips push it toward a
    /// distributed promotion. So debit and credit are looked up together.</summary>
    private async Task<(Guid? Debit, Guid? Credit)> ResolvePairAsync(string debitCode, string creditCode)
    {
        if (string.IsNullOrWhiteSpace(debitCode) || string.IsNullOrWhiteSpace(creditCode)) return (null, null);
        var accounts = _accounts;
        var found = await accounts.GetRecordsAsync($"Code = '{debitCode}' OR Code = '{creditCode}'");
        // An unpostable account (group) is filtered here too: to the caller this
        // is indistinguishable from "no account", and that is correct — there is
        // nothing to post to in either case. A setting with such a code is caught
        // by the profile handler, where the refusal is visible to the person.
        return (found.FirstOrDefault(a => a.Code == debitCode && a.IsPostable)?.MetaId,
                found.FirstOrDefault(a => a.Code == creditCode && a.IsPostable)?.MetaId);
    }

    /// <summary>Fiscal period covering the date; null if there is no such period.</summary>
    public async Task<Guid?> ResolvePeriodAsync(DateTime date)
        => (await ResolvePeriodRecordAsync(date))?.MetaId;

    /// <summary>
    /// Fiscal-period record covering the date. SEVERAL matching periods —
    /// corrupted master data: monthly reporting would depend on row order in the
    /// dictionary. That is not silently allowed as "take the first" — the refusal
    /// names both periods so the setting can be fixed (exactly as TaxService
    /// treats overlapping rates).
    /// </summary>
    private async Task<FiscalPeriod?> ResolvePeriodRecordAsync(DateTime date)
    {
        var d = date.Date;
        var periods = _periods;
        var matching = (await periods.GetRecordsAsync())
            .Where(p => d >= p.FromDate.Date && d <= p.ToDate.Date)
            .ToList();

        if (matching.Count == 0) return null;
        if (matching.Count > 1)
            throw new InvalidOperationException(
                $"На {d:yyyy-MM-dd} приходится больше одного учётного периода (" +
                string.Join(", ", matching.Select(p => p.Code)) +
                "). Окна периодов пересекаться не должны.");

        return matching[0];
    }

    /// <summary>Post a balanced Dr/Cr journal entry by account CODES from the profile.
    /// Returns the journal-entry id or null if posting is impossible OR this fact
    /// is already posted.
    ///
    /// Circuits: trade postings write the financial and management books by
    /// default (FIN,MGT). Tax legs pass "FIN,TAX" so the management book stays
    /// net of VAT — tax lives in a separate journal entry in FIN+TAX.
    /// </summary>
    public async Task<Guid?> PostAsync(
        DateTime date, Guid legalEntity, Guid currency, decimal amount,
        string debitAccountCode, string creditAccountCode,
        string description, string debitLineText, string creditLineText,
        string? circuits = null)
    {
        if (amount <= 0m || legalEntity == Guid.Empty) return null;

        // ONE DESCRIPTION — ONE JOURNAL ENTRY. The description carries the source
        // document id and the purpose ("Sales invoice <id>", "Cost of sales <id>",
        // "Purchase order <id>"), so a repeat means re-posting THE SAME fact, not
        // a second fact.
        //
        // The guard is not theoretical: the document's after-post event runs
        // TWICE when its own posting appends movements through the manager — that
        // is what the CostingIssue driver does when it writes off sold cost.
        // Without this check any sale of an item that has cost layers doubled both
        // revenue and COGS in the book (caught by CostOfSalesGLTest; the old
        // SalesGLPostingTest missed it because it seeds stock with a direct
        // register movement — nothing to write off, and one event was enough).
        //
        // Unpost and re-post of the document also land here: blocking a repeat
        // there is CORRECT — the first journal entry was never reversed, it
        // stayed in the book.
        var alreadyPosted = await _documents.CountDocumentsAsync<JournalEntry>(
            $"Description = '{description.Replace("'", "''")}'");
        if (alreadyPosted > 0) return null;

        var (debit, credit) = await ResolvePairAsync(debitAccountCode, creditAccountCode);
        if (debit == null || credit == null) return null;

        var period = await ResolvePeriodRecordAsync(date);
        if (period == null) return null;

        // Closed month: the predicate lives on IFiscalPeriodService (and the
        // platform IFiscalCalendar hook). Here it is a quiet skip — PostAsync is
        // best-effort. The loud refuse is DocumentPostingService, which calls
        // the same service before any register movement is written or reversed.
        if (await ScriptServices.Get<IFiscalPeriodService>().ClosedReasonAsync(date) != null)
            return null;

        // The journal entry is created by the typed document manager: it issues
        // MetaId and a number from the sequence, runs OnBeforeCreate/OnBeforeInsert
        // and required-field validation, and writes lines from the table part.
        // The transition to Posted runs GLPostingTx → movements on the GL register.
        var documents = _documents;
        var entry = await documents.NewDocumentAsync<JournalEntry>("Draft", new Dictionary<string, object?>
        {
            ["DocumentDate"] = date.Date,
            ["LegalEntity"] = legalEntity,
            ["FiscalPeriod"] = period.MetaId,
            ["Currency"] = currency,
            ["Description"] = description,
            ["Circuits"] = string.IsNullOrWhiteSpace(circuits) ? "FIN,MGT" : circuits,
        });

        entry.Lines.Add(new JournalEntryLinesTablePartRow
        {
            Account = debit.Value, Debit = amount, Credit = 0m, Description = debitLineText,
        });
        entry.Lines.Add(new JournalEntryLinesTablePartRow
        {
            Account = credit.Value, Debit = 0m, Credit = amount, Description = creditLineText,
        });

        await documents.SaveDocumentAsync(entry);

        await _posting
            .SetSubtypeAsync(JournalEntryType, entry.MetaId, "Posted");
        return entry.MetaId;
    }
}
