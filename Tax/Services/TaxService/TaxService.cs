using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Сервис "TaxService": контракт ITaxService. Налоговый расчёт в одном месте —
// подбор ДЕЙСТВУЮЩЕЙ ставки по налоговому коду и дате (TaxCode → Tax → TaxRate)
// и сумма налога (база × ставка, округлённая до денежной точности). Ставка
// хранится долей (0.15 = 15%), точность денег — глобальная настройка AmountScale.
//
// ДАТИРОВАНИЕ. Окно действия несут ВСЕ ТРИ справочника контура — Tax, TaxCode и
// TaxRate: у каждого EffectiveFrom (обязательное) и EffectiveTo (необязательное,
// NULL = «бессрочно»). Ставка применима к документу, когда дата документа лежит
// в окне всех трёх: отменённый налог ставки не даёт, вышедший из употребления
// код ставки не даёт, а сама ставка берётся та, что действовала В ТУ ДАТУ, —
// не последняя заведённая.
//
// ПОЧЕМУ ставка ищется по налогу, а не читается из TaxCode.TaxRate. История
// ставок налога — это и есть строки TaxRate с общим Tax и непересекающимися
// окнами («historical rate is immutable» в описании справочника). TaxCode.TaxRate
// фиксирует ставку, актуальную на момент ЗАВЕДЕНИЯ кода, и по построению
// устаревает при первом же её изменении; выбирать по нему — значит считать
// прошлогодний счёт по сегодняшней ставке. Развести версии самим КОДОМ тоже
// нельзя: TaxCode.Code объявлен уникальным, второй строки с тем же кодом на
// новый период не завести. Поэтому TaxCode.TaxRate — исходная привязка кода к
// налогу, а не ответ на вопрос «сколько процентов на эту дату».
//
// CalculateTax — СИНХРОННЫЙ (годится для проводок). ResolveRateAsync — async
// (чтение справочников), для событий/команд/отчётов/API.
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

    /// <summary>Тип документа TaxCalculation — цель перевода в Finalized.</summary>
    private static readonly Guid TaxCalculationType = Guid.Parse("1e07e7a9-d80f-4067-bc65-e40c96d4feee");

    /// <summary>
    /// Окно действия: запись применима к дате, когда EffectiveFrom ≤ дата ≤ EffectiveTo.
    /// Границы ВКЛЮЧИТЕЛЬНЫ — EffectiveTo подписано «действует ПО», а не «до».
    /// NULL с любой стороны означает открытое окно; EffectiveFrom сейчас объявлено
    /// обязательным во всех трёх справочниках, но предикат на это не опирается:
    /// обязательность — свойство метаданных, а не закон предметной области.
    /// Сравниваются КАЛЕНДАРНЫЕ дни: ставка, закрытая 31.12, обязана покрывать
    /// документ от 31.12 14:00.
    /// </summary>
    private static bool IsEffectiveOn(DateTime? from, DateTime? to, DateTime date)
        => (from is null || from.Value.Date <= date.Date)
        && (to is null || date.Date <= to.Value.Date);

    /// <summary>
    /// Код налога по умолчанию из настроек модуля; null, если контур не настроен.
    /// Это вопрос КОНФИГУРАЦИИ, а не даты: Code объявлен уникальным, строка ровно
    /// одна. Действует ли она на дату документа — решает ResolveRateAsync, чтобы
    /// «контур не настроен» (налога нет, это норма) и «настроен, но на эту дату не
    /// действует» (налог потерян, это авария) не сливались в один и тот же null.
    /// </summary>
    public async Task<Guid?> ResolveDefaultTaxCodeAsync()
    {
        var settings = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (settings is null || string.IsNullOrWhiteSpace(settings.DefaultTaxCode)) return null;
        return (await _codes.GetRecordsAsync($"Code = '{settings.DefaultTaxCode}'")).FirstOrDefault()?.MetaId;
    }

    /// <summary>
    /// ДВИЖОК ПРАВИЛ: какое правило определения налога срабатывает на этот контекст
    /// сделки и эту дату. Возвращает САМО ПРАВИЛО, а не только код, — вызывающему
    /// нужен и код (<c>TaxCode</c>), и объяснение (<c>Code</c>/<c>Name</c>), иначе
    /// «почему тут 15%» становится вопросом без ответа. Null — не сработало ни одно.
    ///
    /// КОНТЕКСТ — СЛОВАРЬ, А НЕ КЛАСС. Контракты сервисов собираются отдельной
    /// сборкой, которая не видит типов, объявленных в скриптах: класс
    /// TaxTransactionContext в публичной сигнатуре сломал бы контракты ВСЕХ
    /// сервисов стенда. Словарь плоских путей («buyer.type», «item.group»,
    /// «amount») эту границу переживает — и заодно оставляет движок развязанным с
    /// документом: Sales кладёт своё, Purchasing своё, а движок про них не знает.
    ///
    /// ПОРЯДОК РАЗБОРА. Правила сортируются по Priority (меньше — раньше), при
    /// равенстве — по более позднему EffectiveFrom, затем по числу условий: из двух
    /// одинаково приоритетных выигрывает БОЛЕЕ СПЕЦИФИЧНОЕ. Иначе исход зависел бы
    /// от порядка строк в таблице, то есть был бы случайным.
    /// </summary>
    public async Task<TaxRule?> ResolveRuleAsync(Dictionary<string, object?> context, DateTime? taxPointDate = null)
    {
        var taxPoint = (taxPointDate ?? DateTime.UtcNow).Date;

        var candidates = (await _rules.GetRecordsAsync("1 = 1"))
            .Where(r => !r.IsDisabled && IsEffectiveOn(r.EffectiveFrom, r.EffectiveTo, taxPoint))
            .ToList();
        if (candidates.Count == 0) return null;

        // Условия читаются ОДНИМ запросом на все правила-кандидаты, а не по запросу
        // на правило: каждый вызов менеджера — это обращение к БД внутри уже идущей
        // транзакции проведения, и лишние round-trip'ы толкают её к повышению до
        // распределённой (см. GeneralLedgerService).
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
    /// Условия ОДНОЙ группы соединяются И, разные группы — ИЛИ: «(A и B) или (C)».
    /// Правило БЕЗ условий срабатывает всегда — это законный «общий случай»,
    /// который ставят последним приоритетом вместо кода по умолчанию.
    /// </summary>
    private static bool Matches(List<TaxRuleCondition> conditions, Dictionary<string, object?> context)
    {
        if (conditions.Count == 0) return true;

        return conditions
            .GroupBy(c => c.ConditionGroup)
            .Any(group => group.All(c => Evaluate(c, context)));
    }

    /// <summary>
    /// Одно условие: значение из контекста против эталона, оператором из
    /// перечисления. Набор операторов ЗАКРЫТ и живёт в метаданных
    /// (<c>TaxRuleOperator</c>), а не белым списком строк в этом коде: правило с
    /// опечаткой в операторе невозможно завести в принципе.
    ///
    /// Сравнение строк — регистронезависимое и без пробелов по краям: коды в
    /// справочниках и в правилах заводят руками, и «B2B» против «b2b » не должно
    /// решать судьбу налога.
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

        // Числовые операторы. Значение, которое числом не читается, условие не
        // выполняет — молча, а не исключением: одно кривое правило не должно
        // ронять проведение документа, к которому оно даже не относится.
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

    /// <summary>Инвариантная культура: правило «сумма &gt; 1000.50» обязано читаться
    /// одинаково на любом стенде, а не зависеть от локали сервера.</summary>
    private static bool TryNumber(string? text, out decimal value)
        => decimal.TryParse((text ?? string.Empty).Trim(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Порождает ПРОВЕДЁННЫЙ расчёт налога на заданную базу и возвращает его id.
    /// <paramref name="taxPointDate"/> — дата налогового события (дата документа-
    /// источника); по ней же подбирается ставка, иначе документ и его налог
    /// датировались бы по-разному. Не задана — сегодняшний день.
    ///
    /// Возвращает null, когда налоговый контур не настроен (нет кода по умолчанию,
    /// направления или юрлица) — это НЕ ошибка: документ-источник обязан
    /// проводиться как раньше на стенде без налогов. Но контур, который настроен и
    /// при этом не даёт ставки на дату документа, — ОШИБКА, и она бросается:
    /// см. RequireRateAsync.
    ///
    /// Здесь, а не в обработчиках счёта и прихода, потому что вход и выход
    /// отличаются ровно кодом направления — всё остальное совпадает, и разъехаться
    /// им нельзя: и то и другое попадает в один леджер и одну декларацию.
    /// </summary>
    public async Task<Guid?> CreateCalculationAsync(
        Guid legalEntity, string directionCode, decimal taxBase, string reason,
        DateTime? taxPointDate = null, Dictionary<string, object?>? context = null)
    {
        if (taxBase <= 0m || legalEntity == Guid.Empty) return null;

        // ОДНА ПРИЧИНА — ОДИН РАСЧЁТ. reason несёт документ-источник ("Sales invoice
        // <номер>"), поэтому повтор означает повторное определение ТОГО ЖЕ налога.
        //
        // Защита обязательна, а не на всякий случай: событие after-post
        // документа-источника выполняется ДВАЖДЫ, когда его же проведение дописывает
        // движения через менеджер, — а именно это делает драйвер CostingIssue,
        // списывая себестоимость проданного. Без проверки КАЖДАЯ продажа товара со
        // слоями себестоимости заводила два расчёта, удваивая выходной налог и в
        // леджере, и в декларации (поймано SalesOutputTaxTest —
        // CostLayersDoNotDuplicateOutputTax; обычные тесты этого не видят, потому
        // что заводят остаток прямым движением регистра, и списывать нечего).
        var already = await _documents.CountDocumentsAsync<TaxCalculation>(
            $"DeterminationReason = '{reason.Replace("'", "''")}'");
        if (already > 0) return null;

        var taxPoint = (taxPointDate ?? DateTime.UtcNow).Date;

        // КОД ОПРЕДЕЛЯЕТ ПРАВИЛО, а настройка — только когда правила молчат.
        // Порядок именно такой: правила — это данные, которые заводит бухгалтер под
        // свою страну и свои сделки, а DefaultTaxCode — одна строка на весь стенд.
        // Обратная совместимость при этом полная: контекст не передали (или правил
        // нет) — поведение ровно прежнее, поэтому включение движка не трогает уже
        // работающие стенды.
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
            // Сработавшее правило пишется НА РАСЧЁТ: правило потом отредактируют или
            // выключат, а расчёт неизменен и обязан сам объяснять свою ставку.
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

    /// <summary>Сумма налога = база × ставка (доля), округлённая до денежной точности.</summary>
    public decimal CalculateTax(decimal baseAmount, decimal rate)
        => Math.Round(baseAmount * rate, GlobalConstants.Get<int?>("AmountScale") ?? 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Ставка, ДЕЙСТВУЮЩАЯ на дату (по умолчанию — сегодня): TaxCode → Tax → TaxRate.
    ///
    /// null означает «на эту дату ставки нет» и покрывает четыре случая: кода не
    /// существует; код вне своего окна; налог вне своего окна; ни одна строка
    /// TaxRate этого налога дату не покрывает. Это ответ ЗАПРОСА, а не разрешение
    /// посчитать налог нулём — считающие методы на нём отказывают
    /// (RequireRateAsync), потому что «ставки нет» и «ставка 0%» — разные вещи, а
    /// молча выпущенный документ без налога всплывает только у налогового органа.
    ///
    /// НЕСКОЛЬКО подходящих строк — порча данных: окна ставок одного налога
    /// обязаны не пересекаться. Это НЕ разрешается молча, «взять последнюю»: часть
    /// документов посчиталась бы по одной ставке, часть по другой, и разошлось бы
    /// это только в декларации. Отказ называет обе ставки, чтобы настройку можно
    /// было починить.
    /// </summary>
    public async Task<decimal?> ResolveRateAsync(Guid taxCodeId, DateTime? onDate = null)
    {
        var date = (onDate ?? DateTime.UtcNow).Date;

        var code = await _codes.GetRecordAsync(taxCodeId);
        if (code is null || code.Tax == Guid.Empty) return null;
        if (!IsEffectiveOn(code.EffectiveFrom, code.EffectiveTo, date)) return null;

        var tax = await _taxes.GetRecordAsync(code.Tax);
        if (tax is null || !IsEffectiveOn(tax.EffectiveFrom, tax.EffectiveTo, date)) return null;

        // Отбор по налогу уходит в SQL, окно проверяется в памяти: строк истории
        // ставок у одного налога единицы, а датный литерал внутри строки-фильтра
        // зависел бы от диалекта БД (стенд живёт и на SQL Server, и на PostgreSQL)
        // и от языковых настроек сервера.
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

    /// <summary>Сумма налога по коду на дату: подбирает действующую ставку и считает.
    /// Ставки на дату нет — ОТКАЗ, а не ноль (ноль неотличим от «не облагается»).</summary>
    public async Task<decimal> CalculateByCodeAsync(decimal baseAmount, Guid taxCodeId, DateTime? onDate = null)
        => CalculateTax(baseAmount, await RequireRateAsync(taxCodeId, (onDate ?? DateTime.UtcNow).Date));

    /// <summary>
    /// Ставка на дату — или отказ. Дверь для путей, которые ОБЯЗАНЫ получить число:
    /// вернуть здесь null значит выпустить документ без налога и не сказать об этом
    /// никому.
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
