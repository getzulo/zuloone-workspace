using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Service "TaxService": ITaxService contract. Tax calculation in one place —
// resolve the EFFECTIVE rate by tax code and date (TaxCode → Tax → TaxRate)
// and the tax amount (base × rate, rounded to money precision). The rate is
// stored as a fraction (0.15 = 15%); money precision is the global AmountScale.
//
// DATING. The effective window is carried by ALL THREE contour dictionaries —
// Tax, TaxCode and TaxRate: each has EffectiveFrom (required) and EffectiveTo
// (optional, NULL = "open-ended"). A rate applies to a document when the
// document date sits in all three windows: a cancelled tax yields no rate, a
// retired code yields no rate, and the rate itself is the one effective ON
// THAT DATE — not the last one created.
//
// WHY the rate is looked up by tax, not read from TaxCode.TaxRate. A tax's
// rate history is the TaxRate rows that share a Tax and have non-overlapping
// windows ("historical rate is immutable" in the dictionary description).
// TaxCode.TaxRate records the rate current at CODE CREATION and, by construction,
// goes stale on the first change; picking by it would calculate last year's
// invoice at today's rate. Splitting versions by the CODE itself is also
// impossible: TaxCode.Code is unique, a second row with the same code for a
// new period cannot be created. So TaxCode.TaxRate is the original binding of
// the code to the tax, not the answer to "how many percent on this date".
//
// CalculateTax is SYNCHRONOUS (safe for postings). ResolveRateAsync is async
// (dictionary reads), for events / commands / reports / API.
public partial class TaxService
{
    private readonly IDictionaryManager<Tax> _taxes;
    private readonly IDictionaryManager<TaxCode> _codes;
    private readonly IDictionaryManager<TaxRate> _rates;
    private readonly IDictionaryManager<TaxSettings> _settings;
    private readonly IDictionaryManager<TaxDirection> _directions;
    private readonly IDictionaryManager<LegalEntity> _legalEntities;
    private readonly IDictionaryManager<TaxRule> _rules;
    private readonly IDictionaryManager<TaxRuleCondition> _ruleConditions;
    private readonly IDocumentManager _documents;
    private readonly IDocumentPostingService _posting;

    public TaxService(
        IDictionaryManager<Tax> taxes,
        IDictionaryManager<TaxCode> codes,
        IDictionaryManager<TaxRate> rates,
        IDictionaryManager<TaxSettings> settings,
        IDictionaryManager<TaxDirection> directions,
        IDictionaryManager<LegalEntity> legalEntities,
        IDictionaryManager<TaxRule> rules,
        IDictionaryManager<TaxRuleCondition> ruleConditions,
        IDocumentManager documents,
        IDocumentPostingService posting)
    {
        _taxes = taxes;
        _codes = codes;
        _rates = rates;
        _settings = settings;
        _directions = directions;
        _legalEntities = legalEntities;
        _rules = rules;
        _ruleConditions = ruleConditions;
        _documents = documents;
        _posting = posting;
    }

    /// <summary>TaxCalculation document type — the target of the Finalized transition.</summary>
    private static readonly Guid TaxCalculationType = Guid.Parse("1e07e7a9-d80f-4067-bc65-e40c96d4feee");

    /// <summary>
    /// Effective window: a record applies to a date when EffectiveFrom ≤ date ≤ EffectiveTo.
    /// Bounds are INCLUSIVE — EffectiveTo is captioned "effective THROUGH", not "until".
    /// NULL on either side means an open window; EffectiveFrom is currently required
    /// on all three dictionaries, but the predicate does not rely on that:
    /// requiredness is a metadata property, not a domain law.
    /// CALENDAR days are compared: a rate closed on 31.12 must cover a document
    /// from 31.12 14:00.
    /// </summary>
    private static bool IsEffectiveOn(DateTime? from, DateTime? to, DateTime date)
        => (from is null || from.Value.Date <= date.Date)
        && (to is null || date.Date <= to.Value.Date);

    /// <summary>
    /// Default tax code from module settings; null if the contour is not configured.
    /// This is a CONFIGURATION question, not a date: Code is unique, there is exactly
    /// one row. Whether it is effective on the document date is decided by
    /// ResolveRateAsync, so "contour not configured" (no tax, that is normal) and
    /// "configured but not effective on this date" (tax lost, that is an accident)
    /// do not collapse into the same null.
    /// </summary>
    public async Task<Guid?> ResolveDefaultTaxCodeAsync()
    {
        var settings = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (settings is null || string.IsNullOrWhiteSpace(settings.DefaultTaxCode)) return null;
        return (await _codes.GetRecordsAsync($"Code = '{settings.DefaultTaxCode}'")).FirstOrDefault()?.MetaId;
    }

    /// <summary>
    /// RULE ENGINE: which tax-determination rule fires for this transaction
    /// context and this date. Returns THE RULE ITSELF, not just the code — the
    /// caller needs both the code (<c>TaxCode</c>) and an explanation
    /// (<c>Code</c>/<c>Name</c>), otherwise "why 15% here" has no answer. Null —
    /// none fired.
    ///
    /// CONTEXT IS A DICTIONARY, NOT A CLASS. Service contracts are compiled in a
    /// separate assembly that cannot see types declared in scripts: a
    /// TaxTransactionContext class in a public signature would break contracts of
    /// ALL stand services. A dictionary of flat paths ("buyer.type", "item.group",
    /// "amount") survives that boundary — and also keeps the engine decoupled from
    /// the document: Sales puts its own, Purchasing its own, and the engine does
    /// not know about them.
    ///
    /// MATCH ORDER. Rules are sorted by Priority (smaller first), then by later
    /// EffectiveFrom, then by condition count: of two equally prioritized rules
    /// the MORE SPECIFIC wins. Otherwise the outcome would depend on row order
    /// in the table, i.e. be random.
    /// </summary>
    public async Task<TaxRule?> ResolveRuleAsync(Dictionary<string, object?> context, DateTime? taxPointDate = null)
    {
        var taxPoint = (taxPointDate ?? DateTime.UtcNow).Date;

        var candidates = (await _rules.GetRecordsAsync("1 = 1"))
            .Where(r => !r.IsDisabled && IsEffectiveOn(r.EffectiveFrom, r.EffectiveTo, taxPoint))
            .ToList();
        if (candidates.Count == 0) return null;

        // Conditions are read in ONE query for all candidate rules, not one query
        // per rule: each manager call is a DB hit inside an already-open posting
        // transaction, and extra round-trips push it toward a distributed
        // promotion (see GeneralLedgerService).
        var conditions = (await _ruleConditions.GetRecordsAsync("1 = 1"))
            .GroupBy(c => c.TaxRule)
            .ToDictionary(g => g.Key, g => g.ToList());

        var ordered = candidates
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.EffectiveFrom)
            .ThenByDescending(r => conditions.TryGetValue(r.MetaId, out var cs) ? cs.Count : 0);

        foreach (var rule in ordered)
        {
            var own = conditions.TryGetValue(rule.MetaId, out var cs) ? cs : new List<TaxRuleCondition>();
            if (Matches(own, context)) return rule;
        }

        return null;
    }

    /// <summary>
    /// Conditions of ONE group are AND, different groups are OR: "(A and B) or (C)".
    /// A rule WITHOUT conditions always matches — a lawful "catch-all" placed last
    /// by priority instead of a default code.
    /// </summary>
    private static bool Matches(List<TaxRuleCondition> conditions, Dictionary<string, object?> context)
    {
        if (conditions.Count == 0) return true;

        return conditions
            .GroupBy(c => c.ConditionGroup)
            .Any(group => group.All(c => Evaluate(c, context)));
    }

    /// <summary>
    /// One condition: a context value against the expected, with an enum operator.
    /// The operator set is CLOSED and lives in metadata (<c>TaxRuleOperator</c>),
    /// not as a string whitelist in this code: a rule with a typo in the operator
    /// cannot be created at all.
    ///
    /// String comparison is case-insensitive and trimmed: codes in dictionaries
    /// and rules are entered by hand, and "B2B" vs "b2b " must not decide the tax.
    /// </summary>
    private static bool Evaluate(TaxRuleCondition condition, Dictionary<string, object?> context)
    {
        context.TryGetValue(condition.Field ?? string.Empty, out var raw);
        var actual = raw?.ToString();
        var expected = condition.Value;

        switch (condition.Operator)
        {
            case TaxRuleOperator.Exists:
                return !string.IsNullOrWhiteSpace(actual);
            case TaxRuleOperator.NotExists:
                return string.IsNullOrWhiteSpace(actual);
            case TaxRuleOperator.Eq:
                return SameText(actual, expected);
            case TaxRuleOperator.Neq:
                return !SameText(actual, expected);
            case TaxRuleOperator.In:
                return Split(expected).Any(v => SameText(actual, v));
            case TaxRuleOperator.NotIn:
                return !Split(expected).Any(v => SameText(actual, v));
        }

        // Numeric operators. A value that is not a number fails the condition —
        // silently, not by exception: one broken rule must not fail posting of a
        // document it does not even apply to.
        if (!TryNumber(actual, out var left)) return false;

        if (condition.Operator == TaxRuleOperator.Between)
        {
            var bounds = Split(expected);
            if (bounds.Count != 2) return false;
            if (!TryNumber(bounds[0], out var lo) || !TryNumber(bounds[1], out var hi)) return false;
            if (lo > hi) (lo, hi) = (hi, lo);
            return left >= lo && left <= hi;
        }

        if (!TryNumber(expected, out var right)) return false;

        return condition.Operator switch
        {
            TaxRuleOperator.Gt => left > right,
            TaxRuleOperator.Gte => left >= right,
            TaxRuleOperator.Lt => left < right,
            TaxRuleOperator.Lte => left <= right,
            _ => false,
        };
    }

    private static bool SameText(string? a, string? b)
        => string.Equals(a?.Trim() ?? string.Empty, b?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static List<string> Split(string? value)
        => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Invariant culture: a rule "amount &gt; 1000.50" must parse the same
    /// on any stand, not depend on the server locale.</summary>
    private static bool TryNumber(string? text, out decimal value)
        => decimal.TryParse((text ?? string.Empty).Trim(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Spawns a POSTED tax calculation on the given base and returns its id.
    /// <paramref name="taxPointDate"/> is the tax-event date (the source document
    /// date); the rate is resolved on it too, otherwise the document and its tax
    /// would be dated differently. Unspecified — today.
    ///
    /// Returns null when the tax contour is not configured (no default code,
    /// direction or legal entity) — that is NOT an error: the source document
    /// must post as before on a stand without taxes. But a contour that is
    /// configured and still yields no rate on the document date is an ERROR, and
    /// it is thrown: see RequireRateAsync.
    ///
    /// Here, not in the invoice and receipt handlers, because input and output
    /// differ by the direction code alone — everything else matches, and they
    /// must not drift: both land in one ledger and one return.
    /// </summary>
    public async Task<Guid?> CreateCalculationAsync(
        Guid legalEntity, string directionCode, decimal taxBase, string reason,
        DateTime? taxPointDate = null, Dictionary<string, object?>? context = null)
    {
        if (taxBase <= 0m || legalEntity == Guid.Empty) return null;

        // ONE REASON — ONE CALCULATION. reason carries the source document
        // ("Sales invoice <number>"), so a repeat means re-determining THE SAME tax.
        //
        // The guard is required, not just in case: the source document's after-post
        // event runs TWICE when its own posting appends movements through the
        // manager — which is exactly what the CostingIssue driver does when it
        // writes off sold cost. Without the check EVERY sale of an item with cost
        // layers would open two calculations, doubling output tax in both the
        // ledger and the return (caught by SalesOutputTaxTest —
        // CostLayersDoNotDuplicateOutputTax; ordinary tests miss this because they
        // seed stock with a direct register movement, so there is nothing to write
        // off).
        var already = await _documents.CountDocumentsAsync<TaxCalculation>(
            $"DeterminationReason = '{reason.Replace("'", "''")}'");
        if (already > 0) return null;

        var taxPoint = (taxPointDate ?? DateTime.UtcNow).Date;

        // The CODE is determined by a rule; settings apply only when rules are silent.
        // That order is deliberate: rules are data a bookkeeper creates for their
        // country and deals, and DefaultTaxCode is one row for the whole stand.
        // Backward compatibility is complete: no context (or no rules) — behaviour
        // is exactly as before, so turning the engine on does not touch stands
        // already running.
        var matchedRule = context is null ? null : await ResolveRuleAsync(context, taxPoint);
        var taxCode = matchedRule?.TaxCode ?? await ResolveDefaultTaxCodeAsync();
        if (taxCode is null || taxCode == Guid.Empty) return null;

        var rate = await RequireRateAsync(taxCode.Value, taxPoint);

        var direction = (await _directions.GetRecordsAsync($"Code = '{directionCode}'")).FirstOrDefault();
        if (direction is null) return null;

        var le = await _legalEntities.GetRecordAsync(legalEntity);
        if (le is null) return null;

        var calc = await _documents.NewDocumentAsync<TaxCalculation>("Draft", new Dictionary<string, object?>
        {
            ["LegalEntity"] = le.MetaId,
            ["Currency"] = le.Currency,
            ["TaxPointDate"] = taxPoint,
            ["DeterminationReason"] = reason,
            // The matched rule is written ON THE CALCULATION: the rule may later be
            // edited or disabled, and the calculation is immutable and must explain
            // its own rate.
            ["MatchedRule"] = matchedRule?.MetaId,
        });

        calc.Lines.Add(new TaxCalculationLinesTablePartRow
        {
            Direction = direction.MetaId,
            TaxCode = taxCode.Value,
            RateValue = rate,
            TaxBase = taxBase,
            TaxAmount = CalculateTax(taxBase, rate),
        });

        await _documents.SaveDocumentAsync(calc);
        await _posting.SetSubtypeAsync(TaxCalculationType, calc.MetaId, "Finalized");
        return calc.MetaId;
    }

    /// <summary>Tax amount = base × rate (fraction), rounded to money precision.</summary>
    public decimal CalculateTax(decimal baseAmount, decimal rate)
        => Math.Round(baseAmount * rate, GlobalConstants.Get<int?>("AmountScale") ?? 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Rate EFFECTIVE on the date (default — today): TaxCode → Tax → TaxRate.
    ///
    /// null means "no rate on this date" and covers four cases: the code does not
    /// exist; the code is outside its window; the tax is outside its window; no
    /// TaxRate row of this tax covers the date. That is a QUERY answer, not
    /// permission to compute tax as zero — calculating methods refuse on it
    /// (RequireRateAsync), because "no rate" and "0% rate" are different things,
    /// and a silently issued document without tax only surfaces at the tax
    /// authority.
    ///
    /// SEVERAL matching rows — corrupted data: rate windows of one tax must not
    /// overlap. This is NOT silently allowed as "take the last": some documents
    /// would be calculated at one rate, some at another, and it would only
    /// diverge on the return. The refusal names both rates so the setting can
    /// be fixed.
    /// </summary>
    public async Task<decimal?> ResolveRateAsync(Guid taxCodeId, DateTime? onDate = null)
    {
        var date = (onDate ?? DateTime.UtcNow).Date;

        var code = await _codes.GetRecordAsync(taxCodeId);
        if (code is null || code.Tax == Guid.Empty) return null;
        if (!IsEffectiveOn(code.EffectiveFrom, code.EffectiveTo, date)) return null;

        var tax = await _taxes.GetRecordAsync(code.Tax);
        if (tax is null || !IsEffectiveOn(tax.EffectiveFrom, tax.EffectiveTo, date)) return null;

        // Filter by tax goes to SQL; the window is checked in memory: a tax has
        // a handful of rate-history rows, and a date literal inside a filter
        // string would depend on the DB dialect (the stand runs on both SQL Server
        // and PostgreSQL) and on the server language settings.
        var applicable = (await _rates.GetRecordsAsync($"Tax = '{code.Tax}'"))
            .Where(r => IsEffectiveOn(r.EffectiveFrom, r.EffectiveTo, date))
            .ToList();

        if (applicable.Count == 0) return null;
        if (applicable.Count > 1)
            throw new InvalidOperationException(
                $"Налог '{tax.Code}': на {date:yyyy-MM-dd} действует больше одной ставки (" +
                string.Join(", ", applicable.Select(r => $"{r.Code} = {r.Rate}")) +
                "). Окна действия ставок одного налога не должны пересекаться.");

        return applicable[0].Rate;
    }

    /// <summary>
    /// A rate of THE SAME tax whose effective window overlaps the given one — or
    /// null if there is no overlap. <paramref name="excludeRate"/> excludes the
    /// record being checked, so editing an existing rate does not treat itself
    /// as an overlap (when creating a new one — <c>Guid.Empty</c>).
    ///
    /// WHY THE RULE LIVES HERE, NOT IN THE DICTIONARY HANDLER. The condition on
    /// which <see cref="ResolveRateAsync"/> REFUSES to calculate ("more than one
    /// rate matched") and the condition on which a second rate is not allowed to
    /// be created are the same condition. If their definitions drifted even by
    /// one day of the window bound, the dictionary would start accepting an
    /// arrangement on which tax calculation fails — i.e. an input error would
    /// again be caught at invoice issue.
    ///
    /// Both doors stay and that is not duplication: the handler prevents creating
    /// corruption, and the refusal in ResolveRateAsync is the last line of
    /// defence for data loaded past events (import, migration, direct SQL).
    /// </summary>
    public async Task<TaxRate?> FindOverlappingRateAsync(
        Guid tax, Guid excludeRate, DateTime from, DateTime? to)
    {
        if (tax == Guid.Empty) return null;

        // Filter by tax goes to SQL; windows are compared in memory — for the
        // same reason as in ResolveRateAsync: a date literal in a filter string
        // would depend on the DB dialect and the server language settings.
        return (await _rates.GetRecordsAsync($"Tax = '{tax}'"))
            .FirstOrDefault(r => r.MetaId != excludeRate
                && WindowsOverlap(from, to, r.EffectiveFrom, r.EffectiveTo));
    }

    /// <summary>Whether two effective windows overlap. An empty end date means
    /// a window open on the right: "from this date onward".</summary>
    private static bool WindowsOverlap(DateTime aFrom, DateTime? aTo, DateTime bFrom, DateTime? bTo)
        => aFrom.Date <= (bTo?.Date ?? DateTime.MaxValue)
        && bFrom.Date <= (aTo?.Date ?? DateTime.MaxValue);

    /// <summary>Tax amount by code on a date: resolves the effective rate and computes.
    /// No rate on the date — REFUSE, not zero (zero is indistinguishable from "not taxable").</summary>
    public async Task<decimal> CalculateByCodeAsync(decimal baseAmount, Guid taxCodeId, DateTime? onDate = null)
        => CalculateTax(baseAmount, await RequireRateAsync(taxCodeId, (onDate ?? DateTime.UtcNow).Date));

    /// <summary>
    /// Rate on the date — or a refusal. The door for paths that MUST get a number:
    /// returning null here means issuing a document without tax and telling no one.
    /// </summary>
    private async Task<decimal> RequireRateAsync(Guid taxCodeId, DateTime date)
    {
        var rate = await ResolveRateAsync(taxCodeId, date);
        if (rate is not null) return rate.Value;

        var code = await _codes.GetRecordAsync(taxCodeId);
        throw new InvalidOperationException(
            $"Налоговый код '{code?.Code ?? taxCodeId.ToString()}' не имеет ставки, действующей на " +
            $"{date:yyyy-MM-dd}: проверьте окна действия налога, кода и ставок (EffectiveFrom/EffectiveTo).");
    }
}
