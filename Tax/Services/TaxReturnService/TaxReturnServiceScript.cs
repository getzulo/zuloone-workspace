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

    /// <summary>Строка сводки: один налоговый код в одном направлении.</summary>
    private sealed class Line
    {
        public Guid TaxCode;
        public Guid Direction;
        public string DirectionCode = string.Empty;
        public decimal TaxBase;
        public decimal TaxAmount;
    }

    /// <summary>
    /// Собрать декларацию за период и вернуть id созданного документа (черновик).
    /// Границы ВКЛЮЧИТЕЛЬНЫЕ — «с 1 по 31 января» означает, что 31 января входит:
    /// налоговый период задают датами, а не полуинтервалом, и потерянный последний
    /// день — это потерянные документы.
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
