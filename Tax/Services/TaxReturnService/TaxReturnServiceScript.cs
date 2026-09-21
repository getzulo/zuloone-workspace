using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;

// Сборка налоговой декларации за период.
//
// Декларация — это СДАВАЕМЫЙ ДОКУМЕНТ, а не расчёт на лету: то, что отправлено в
// налоговый орган, должно быть видно ровно в том виде, в каком отправлено, и
// после сдачи не меняться (подтип Filed помечен isReadOnly). Поэтому BuildAsync
// не возвращает сводку — он СОЗДАЁТ документ TaxReturn в черновике и отдаёт его
// идентификатор.
//
// Отдельно: сводные типы здесь ПРИВАТНЫЕ намеренно. Публичные методы сервиса
// образуют контракт I<Имя>, который собирается в отдельную сборку РАНЬШЕ моделей
// и типов из скрипта не видит; вложенный DTO в сигнатуре ломает компиляцию
// контрактов — и не своего сервиса, а ВСЕХ сразу.
//
// Разрезы TaxLedger (код налога, направление, юрлицо) — ДИНАМИЧЕСКИЕ аналитики:
// в строке движения лежит не значение, а ссылка на неизменяемый набор значений
// (AnalyticSetMetaId). Поэтому «сгруппировать по коду» не выражается фильтром по
// колонке: движения читаются за период, наборы разворачиваются пакетом через
// AnalyticSetService.ExpandAsync, и группировка идёт уже в памяти.
//
// Налог к уплате = ВЫХОДНОЙ − ВХОДНОЙ. Знак берётся из направления, а не из знака
// суммы: в леджер обе стороны пишутся положительными, и вычитание входного —
// это правило декларации, а не свойство данных.
public partial class TaxReturnService
{
    private static readonly Guid TaxLedgerRegister = Guid.Parse("6955f3f7-088a-418e-bf6d-a37eedfe16b8");

    private const string OutputDirection = "OUTPUT";
    private const string InputDirection = "INPUT";

    private readonly IRegisterMovementService _movements;
    private readonly AnalyticSetService _analytics;
    private readonly IDictionaryManager<TaxDirection> _directions;
    private readonly IDictionaryManager<TaxPeriod> _periods;
    private readonly IDictionaryManager<TaxReportMapping> _mappings;
    private readonly IDocumentManager _documents;

    public TaxReturnService(
        IRegisterMovementService movements,
        AnalyticSetService analytics,
        IDictionaryManager<TaxDirection> directions,
        IDictionaryManager<TaxPeriod> periods,
        IDictionaryManager<TaxReportMapping> mappings,
        IDocumentManager documents)
    {
        _movements = movements;
        _analytics = analytics;
        _directions = directions;
        _periods = periods;
        _mappings = mappings;
        _documents = documents;
    }

    /// <summary>Строка сводки: один налоговый код в одном направлении.</summary>
    private sealed class Line
    {
        public Guid TaxCode;
        public Guid Direction;
        public string DirectionCode = string.Empty;
        public decimal TaxBase;
        public decimal TaxAmount;
        public decimal RecoverableAmount;
    }

    /// <summary>
    /// Собрать декларацию за период и вернуть id созданного документа (черновик).
    /// Границы ВКЛЮЧИТЕЛЬНЫЕ — «с 1 по 31 января» означает, что 31 января входит:
    /// налоговый период задают датами, а не полуинтервалом, и потерянный последний
    /// день — это потерянные документы.
    /// </summary>
    public Task<Guid> BuildAsync(Guid legalEntity, DateTime periodFrom, DateTime periodTo)
        => BuildCoreAsync(legalEntity, periodFrom, periodTo, taxPeriod: Guid.Empty);

    /// <summary>Same as <see cref="BuildAsync"/>, but dates and the period
    /// reference come from a named <c>TaxPeriod</c>. A closed period refuses.</summary>
    public async Task<Guid> BuildFromPeriodAsync(Guid taxPeriodId)
    {
        var period = await _periods.GetRecordAsync(taxPeriodId)
            ?? throw new InvalidOperationException("Налоговый период не найден");
        if (period.IsClosed)
            throw new InvalidOperationException(
                $"Налоговый период «{period.Code}» закрыт — новую декларацию по нему собрать нельзя");
        return await BuildCoreAsync(
            period.LegalEntity, period.FromDate, period.ToDate, period.MetaId);
    }

    private async Task<Guid> BuildCoreAsync(
        Guid legalEntity, DateTime periodFrom, DateTime periodTo, Guid taxPeriod)
    {
        var from = periodFrom.Date;
        var to = periodTo.Date;
        await RefuseIfClosedAsync(legalEntity, from, to, taxPeriod);

        var lines = await CollectAsync(legalEntity, from, to);
        var boxes = await ResolveBoxesAsync(lines, to);

        var outputTax = lines.Where(l => IsDirection(l, OutputDirection)).Sum(l => l.TaxAmount);
        var inputTax = lines.Where(l => IsDirection(l, InputDirection)).Sum(l => l.RecoverableAmount);

        var header = new Dictionary<string, object?>
        {
            ["LegalEntity"] = legalEntity,
            ["PeriodFrom"] = from,
            ["PeriodTo"] = to,
            ["OutputTax"] = outputTax,
            ["InputTax"] = inputTax,
            ["NetPayable"] = outputTax - inputTax,
        };
        if (taxPeriod != Guid.Empty)
            header["TaxPeriod"] = taxPeriod;

        var doc = await _documents.NewDocumentAsync<TaxReturn>("Draft", header);

        foreach (var line in lines.OrderBy(l => l.DirectionCode).ThenBy(l => l.TaxCode))
        {
            boxes.TryGetValue((line.TaxCode, line.Direction), out var box);
            doc.Lines.Add(new TaxReturnLinesTablePartRow
            {
                TaxCode = line.TaxCode,
                Direction = line.Direction,
                TaxBase = line.TaxBase,
                TaxAmount = IsDirection(line, InputDirection) ? line.RecoverableAmount : line.TaxAmount,
                ReturnBox = box ?? string.Empty,
            });
        }

        await _documents.SaveDocumentAsync(doc);
        return doc.MetaId;
    }

    /// <summary>A closed TaxPeriod covering these dates of this entity refuses
    /// a new return — otherwise filing one window twice is silent.</summary>
    private async Task RefuseIfClosedAsync(
        Guid legalEntity, DateTime from, DateTime to, Guid exceptPeriod)
    {
        var closed = (await _periods.GetRecordsAsync($"LegalEntity = '{legalEntity}'"))
            .FirstOrDefault(p => p.IsClosed
                && p.MetaId != exceptPeriod
                && from <= p.ToDate.Date
                && p.FromDate.Date <= to);
        if (closed is null) return;
        throw new InvalidOperationException(
            $"Налоговый период «{closed.Code}» закрыт " +
            $"({closed.FromDate:yyyy-MM-dd} — {closed.ToDate:yyyy-MM-dd}). " +
            "Новую декларацию за эти даты собрать нельзя.");
    }

    /// <summary>
    /// Box letters for each (code, direction) on the period-to date. A directed
    /// mapping beats an undirected one. Several ReturnTypes (SA and UA packs on
    /// one stand) that disagree on the letter leave the box empty rather than
    /// pick a country in Core.
    /// </summary>
    private async Task<Dictionary<(Guid Code, Guid Direction), string>> ResolveBoxesAsync(
        List<Line> lines, DateTime on)
    {
        var result = new Dictionary<(Guid Code, Guid Direction), string>();
        if (lines.Count == 0) return result;

        var maps = (await _mappings.GetRecordsAsync("1 = 1"))
            .Where(m => IsEffectiveOn(m.EffectiveFrom, m.EffectiveTo, on))
            .ToList();
        if (maps.Count == 0) return result;

        foreach (var line in lines)
        {
            var own = maps.Where(m => m.TaxCode == line.TaxCode).ToList();
            var directed = own.Where(m => m.Direction == line.Direction).ToList();
            var chosen = directed.Count > 0
                ? directed
                : own.Where(m => m.Direction == Guid.Empty).ToList();
            if (chosen.Count == 0) continue;
            var boxes = chosen.Select(m => m.ReturnBox)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (boxes.Count == 1)
                result[(line.TaxCode, line.Direction)] = boxes[0];
        }

        return result;
    }

    private static bool IsEffectiveOn(DateTime? from, DateTime? to, DateTime date)
        => (from is null || from.Value.Date <= date.Date)
        && (to is null || date.Date <= to.Value.Date);

    /// <summary>Движения периода, свёрнутые в пары (код, направление).</summary>
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

            // Чужое юрлицо в этой декларации не участвует.
            if (ValueGuid(values, "LegalEntity") != legalEntity) continue;

            var code = ValueGuid(values, "TaxCode");
            var direction = ValueGuid(values, "TaxDirection");
            if (code == Guid.Empty || direction == Guid.Empty) continue;

            var key = (code, direction);
            if (!grouped.TryGetValue(key, out var line))
                grouped[key] = line = new Line { TaxCode = code, Direction = direction };

            line.TaxBase += Decimal(movement, "TaxBase");
            line.TaxAmount += Decimal(movement, "TaxAmount");
            line.RecoverableAmount += Decimal(movement, "RecoverableAmount");
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
