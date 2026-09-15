using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;

// Build a tax return for a period.
//
// A return is a FILABLE DOCUMENT, not an on-the-fly calculation: what was sent
// to the tax authority must be visible exactly as sent, and must not change
// after filing (the Filed subtype is marked isReadOnly). So BuildAsync does not
// return a summary — it CREATES a TaxReturn document as a draft and hands back
// its id.
//
// Separately: summary types here are PRIVATE on purpose. Public service methods
// form the I<Name> contract, which is compiled in a separate assembly BEFORE
// models and cannot see script types; a nested DTO in a signature breaks
// contract compilation — and not of this service, but of ALL of them at once.
//
// TaxLedger slices (tax code, direction, legal entity) are DYNAMIC analytics:
// a movement row holds not a value but a reference to an immutable value set
// (AnalyticSetMetaId). So "group by code" is not a column filter: movements are
// read for the period, sets are expanded in a batch via
// AnalyticSetService.ExpandAsync, and grouping happens in memory.
//
// Tax payable = OUTPUT − INPUT. The sign comes from the direction, not from the
// amount sign: both sides are written to the ledger as positives, and subtracting
// input is a return rule, not a property of the data.
public partial class TaxReturnService
{
    private static readonly Guid TaxLedgerRegister = Guid.Parse("6955f3f7-088a-418e-bf6d-a37eedfe16b8");

    private const string OutputDirection = "OUTPUT";
    private const string InputDirection = "INPUT";

    private readonly IRegisterMovementService _movements;
    private readonly AnalyticSetService _analytics;
    private readonly IDictionaryManager<TaxDirection> _directions;
    private readonly IDocumentManager _documents;

    public TaxReturnService(
        IRegisterMovementService movements,
        AnalyticSetService analytics,
        IDictionaryManager<TaxDirection> directions,
        IDocumentManager documents)
    {
        _movements = movements;
        _analytics = analytics;
        _directions = directions;
        _documents = documents;
    }

    /// <summary>Summary line: one tax code in one direction.</summary>
    private sealed class Line
    {
        public Guid TaxCode;
        public Guid Direction;
        public string DirectionCode = string.Empty;
        public decimal TaxBase;
        public decimal TaxAmount;
    }

    /// <summary>
    /// Build a return for a period and return the created document id (draft).
    /// Bounds are INCLUSIVE — "from 1 through 31 January" means 31 January is in:
    /// a tax period is given as dates, not a half-interval, and a lost last day
    /// is lost documents.
    /// </summary>
    public async Task<Guid> BuildAsync(Guid legalEntity, DateTime periodFrom, DateTime periodTo)
    {
        var from = periodFrom.Date;
        var to = periodTo.Date;

        var lines = await CollectAsync(legalEntity, from, to);

        var outputTax = lines.Where(l => IsDirection(l, OutputDirection)).Sum(l => l.TaxAmount);
        var inputTax = lines.Where(l => IsDirection(l, InputDirection)).Sum(l => l.TaxAmount);

        var doc = await _documents.NewDocumentAsync<TaxReturn>("Draft", new Dictionary<string, object?>
        {
            ["LegalEntity"] = legalEntity,
            ["PeriodFrom"] = from,
            ["PeriodTo"] = to,
            ["OutputTax"] = outputTax,
            ["InputTax"] = inputTax,
            ["NetPayable"] = outputTax - inputTax,
        });

        foreach (var line in lines.OrderBy(l => l.DirectionCode).ThenBy(l => l.TaxCode))
        {
            doc.Lines.Add(new TaxReturnLinesTablePartRow
            {
                TaxCode = line.TaxCode,
                Direction = line.Direction,
                TaxBase = line.TaxBase,
                TaxAmount = line.TaxAmount,
            });
        }

        await _documents.SaveDocumentAsync(doc);
        return doc.MetaId;
    }

    /// <summary>Period movements folded into (code, direction) pairs.</summary>
    private async Task<List<Line>> CollectAsync(Guid legalEntity, DateTime from, DateTime to)
    {
        var upper = to.AddDays(1).AddTicks(-1);

        var movements = await _movements.QueryMovementsAsync(
            TaxLedgerRegister,
            $"[MovementDate] >= '{from:yyyy-MM-dd HH:mm:ss}' AND [MovementDate] <= '{upper:yyyy-MM-dd HH:mm:ss}'");
        if (movements.Count == 0) return new List<Line>();

        var setIds = movements
            .Select(m => AsGuid(m, "AnalyticSetMetaId"))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var sets = await _analytics.ExpandAsync(setIds);

        var grouped = new Dictionary<(Guid Code, Guid Direction), Line>();

        foreach (var movement in movements)
        {
            var setId = AsGuid(movement, "AnalyticSetMetaId");
            if (setId == Guid.Empty || !sets.TryGetValue(setId, out var values)) continue;

            // Another legal entity does not participate in this return.
            if (ValueGuid(values, "LegalEntity") != legalEntity) continue;

            var code = ValueGuid(values, "TaxCode");
            var direction = ValueGuid(values, "TaxDirection");
            if (code == Guid.Empty || direction == Guid.Empty) continue;

            var key = (code, direction);
            if (!grouped.TryGetValue(key, out var line))
                grouped[key] = line = new Line { TaxCode = code, Direction = direction };

            line.TaxBase += Decimal(movement, "TaxBase");
            line.TaxAmount += Decimal(movement, "TaxAmount");
        }

        foreach (var line in grouped.Values)
            line.DirectionCode = (await _directions.GetRecordAsync(line.Direction))?.Code ?? string.Empty;

        return grouped.Values.ToList();
    }

    private static bool IsDirection(Line line, string code)
        => string.Equals(line.DirectionCode, code, StringComparison.OrdinalIgnoreCase);

    private static Guid ValueGuid(IReadOnlyDictionary<string, string> values, string analytic)
        => values.TryGetValue(analytic, out var v) && Guid.TryParse(v, out var g) ? g : Guid.Empty;

    private static Guid AsGuid(IDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var v) || v is null) return Guid.Empty;
        return v is Guid g ? g : Guid.TryParse(v.ToString(), out var p) ? p : Guid.Empty;
    }

    private static decimal Decimal(IDictionary<string, object?> row, string column)
        => row.TryGetValue(column, out var v) && v != null
            ? Convert.ToDecimal(v, CultureInfo.InvariantCulture)
            : 0m;
}
