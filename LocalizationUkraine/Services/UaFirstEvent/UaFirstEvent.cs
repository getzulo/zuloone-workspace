#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Service "UaFirstEvent": перша подія украинского ПДВ (ПКУ 187.1) и база
// единого налога.
//
// ПРАВИЛО ПДВ. Обязательство возникает на дату того из двух событий, что
// случилось РАНЬШЕ: отгрузка или получение денег. В отличие от Саудовской
// Аравии, где налог привязан к счёту, здесь предоплата сама по себе рождает
// обязательство, а последующая отгрузка в её пределах — уже нет.
//
// КАК ЭТО СЧИТАЕТСЯ, И ПОЧЕМУ НЕ СОПОСТАВЛЕНИЕМ. Соблазн — связать конкретную
// оплату с конкретной отгрузкой и смотреть, что из них первое. Этого нельзя
// сделать честно: строка CustomerPayment несёт клиента, договор и сумму, но НЕ
// ссылку на заказ или счёт. Сопоставление пришлось бы выдумать, и оно разъехалось
// бы на первой же частичной оплате.
//
// Вместо этого по договору копятся два итога — сколько отгружено и сколько
// оплачено, — и облагается max из них: больший и есть «события, которые уже
// произошли». Accrued помнит, какая часть базы уже обложена, так что каждое
// событие берёт налог ТОЛЬКО с прироста. Отсюда само собой выходит верное
// поведение во всех четырёх случаях: предоплата облагается сразу; отгрузка после
// неё в её пределах не облагается второй раз; отгрузка без оплаты облагается
// сразу; оплата после отгрузки не облагается повторно.
//
// ЕДИНЫЙ НАЛОГ ЖИВЁТ РЯДОМ, НО СЧИТАЕТСЯ ИНАЧЕ: он кассовый, его база — только
// ДЕНЬГИ, безо всякого max. Поэтому у него свой счётчик обложенного, EpAccrued, и
// своя цель. Складывать их в один Accrued нельзя: у спрощенця 3% базы двух
// налогов расходятся — ЄП берётся с дохода БЕЗ ПДВ, а ПДВ с первого события.
//
// КЛЮЧ — ЮРЛИЦО, КЛИЕНТ И ДОГОВОР, БЕЗ ТОЧКИ. Договор принадлежит ровно одной
// торговой точке, поэтому точка в ключе избыточна. Практическая сторона той же
// медали: строка оплаты точку не несёт, а транзакционный скрипт синхронен и
// достать её из договора не может. В UaVatPayable точка остаётся — её подставляет
// драйвер, который асинхронен. Юрлицо, наоборот, в ключе НУЖНО: режим
// налогообложения — его свойство, а в одной системе живут ТОВ и несколько ФОП.
//
// ГДЕ ЭТО ЗОВУТ. Из драйвера итогов UaVatFirstEvent, уже ПОСЛЕ записи движений:
// прирост зависит от состояния, состояние читается асинхронно, а
// GetTransactions синхронна. Путь «посчитать в событии и проштамповать полем»
// закрыт — поля расширения не попадают в генерируемый класс документа.
public partial class UaFirstEvent
{
    private readonly ITotalsManager _totals;
    private readonly IDictionaryManager<SalesContract> _contracts;
    private readonly IDictionaryManager<TaxCode> _codes;
    private readonly IDictionaryManager<LocalizationUkraineSettings> _settings;
    private readonly IDictionaryManager<UaSingleTaxLimit> _limits;
    private readonly IDictionaryManager<UaRegimeChange> _changes;
    private readonly IDictionaryManager<TaxMapping> _mappings;
    // Создание записи живёт на НЕдженериковом менеджере (NewRecord<T>/
    // SaveRecordAsync<T>), у типизированного его нет вовсе.
    private readonly IDictionaryManager _dictionaries;
    private readonly IDataService _data;

    public UaFirstEvent(
        ITotalsManager totals,
        IDictionaryManager<SalesContract> contracts,
        IDictionaryManager<TaxCode> codes,
        IDictionaryManager<LocalizationUkraineSettings> settings,
        IDictionaryManager<UaSingleTaxLimit> limits,
        IDictionaryManager<UaRegimeChange> changes,
        IDictionaryManager<TaxMapping> mappings,
        IDictionaryManager dictionaries,
        IDataService data)
    {
        _totals = totals;
        _contracts = contracts;
        _codes = codes;
        _settings = settings;
        _limits = limits;
        _changes = changes;
        _mappings = mappings;
        _dictionaries = dictionaries;
        _data = data;
    }

    /// <summary>
    /// Торговая точка договора. Нужна не здесь, а на выходе — в UaVatPayable,
    /// где срез по точкам одного клиента обязан не смешиваться.
    /// </summary>
    public async Task<Guid> OutletOfAsync(Guid contract)
    {
        if (contract == Guid.Empty) return Guid.Empty;
        var record = await _contracts.GetRecordAsync(contract);
        return record?.Outlet ?? Guid.Empty;
    }

    /// <summary>
    /// Юрлицо договора. Нужно ОДНОМУ месту — проверке шапки оплаты: оператор
    /// выбирает юрлицо руками, и разойдись оно с договором, Shipped и Paid легли
    /// бы в разные координаты, а налог начислился бы дважды.
    /// Guid.Empty означает «у договора юрлицо не заполнено» — тогда сверять не с
    /// чем и проверка молчит.
    /// </summary>
    public async Task<Guid> LegalEntityOfAsync(Guid contract)
    {
        if (contract == Guid.Empty) return Guid.Empty;
        var record = await _contracts.GetRecordAsync(contract);
        return record?.LegalEntity ?? Guid.Empty;
    }

    /// <summary>
    /// Режим налогообложения юрлица.
    ///
    /// ЧИТАЕТСЯ СЫРЫМ МЕШКОМ, А НЕ ТИПИЗИРОВАННОЙ ЗАПИСЬЮ, и это вынужденно:
    /// UaTaxRegime — поле РАСШИРЕНИЯ чужого справочника, оно живёт в отдельном
    /// агрегате LegalEntity_LocalizationUkraine и в генерируемый класс
    /// LegalEntity не попадает. Тем же способом Саудовская Аравия достаёт свой
    /// CommercialRegistration.
    ///
    /// ПУСТО ИЛИ НЕ ПРОЧИТАЛОСЬ — ПЛАТЕЛЬЩИК ПДВ. Значение 0 перечисления
    /// выбрано платильщиком ПДВ намеренно: поле необязательное и ненулевое, у
    /// всех существующих юрлиц оно приедет нулём, и нуль обязан означать ровно то
    /// поведение, что было до появления режимов. Иначе обновление молча
    /// перестало бы начислять ПДВ всем.
    /// </summary>
    public async Task<UaTaxRegime> RegimeOfAsync(Guid legalEntity)
    {
        if (legalEntity == Guid.Empty) return UaTaxRegime.VatPayer;

        object? raw = null;
        try
        {
            var bag = await _data.GetByIdAsync("LegalEntity", legalEntity);
            raw = bag?["UaTaxRegime"];
        }
        catch
        {
            // Мешок расширения может не существовать вовсе — модель есть, а
            // строки расширения у этого юрлица ещё нет. Это не ошибка.
            return UaTaxRegime.VatPayer;
        }

        if (raw is null) return UaTaxRegime.VatPayer;
        if (raw is UaTaxRegime typed) return typed;

        // Перечисление переезжает границу данных то числом, то именем — зависит
        // от того, пришла запись через типизированный менеджер или через /api/data.
        if (raw is string name)
            return Enum.TryParse<UaTaxRegime>(name, ignoreCase: true, out var parsed)
                ? parsed
                : UaTaxRegime.VatPayer;

        try
        {
            var number = Convert.ToInt32(raw);
            return Enum.IsDefined(typeof(UaTaxRegime), number)
                ? (UaTaxRegime)number
                : UaTaxRegime.VatPayer;
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            return UaTaxRegime.VatPayer;
        }
    }

    /// <summary>Режим начисляет ПДВ по первому событию?</summary>
    public bool ChargesVat(UaTaxRegime regime)
        => regime is UaTaxRegime.VatPayer or UaTaxRegime.SimplifiedWithVat;

    /// <summary>
    /// Код единого налога для режима — уже разрешённый в запись справочника.
    /// null означает одно из трёх и все три лечатся одинаково (молчанием):
    /// режим единый налог не платит; код в настройках не заполнен; код заполнен,
    /// но такого TaxCode на стенде нет.
    ///
    /// КОД БЕРЁТСЯ ИЗ НАСТРОЕК, А НЕ ЗАШИТ СТРОКОЙ. Зашей «UA-EP3» в скрипт — и
    /// правило заработает ровно на том стенде, где пакет данных ставился как
    /// есть; любой контур, заведённый руками или тестом, молча не найдётся, а
    /// выглядеть это будет как «единый налог просто не начисляется». Ровно так
    /// однажды уже простоял зелёным поставочный seed, который не мог разрешиться.
    /// </summary>
    public async Task<Guid?> SingleTaxCodeIdAsync(UaTaxRegime regime)
    {
        var settings = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();

        var code = regime switch
        {
            UaTaxRegime.SimplifiedWithVat => settings?.SingleTaxCode3,
            UaTaxRegime.SimplifiedNoVat => settings?.SingleTaxCode5,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(code)) return null;

        return (await _codes.GetRecordsAsync($"Code = '{code}'")).FirstOrDefault()?.MetaId;
    }

    /// <summary>
    /// Какая часть базы договора облагается ПДВ ПРЯМО СЕЙЧАС: прирост
    /// max(Shipped, Paid) над уже обложенным. Зовётся ПОСЛЕ записи движений, так
    /// что остатки уже включают текущий документ.
    ///
    /// ВНИМАНИЕ НА АСИММЕТРИЮ. Shipped — база БЕЗ налога (столько стоит товар),
    /// Paid — деньги С налогом (столько пришло на счёт). Сравнивать их напрямую
    /// нельзя: предоплата 120 при ставке 20% закрывает базу 100, а не 120.
    /// Приведение делается здесь, а не в проводке, потому что для него нужна
    /// ставка, а ставка резолвится асинхронно.
    ///
    /// Ноль — законный и частый ответ: отгрузка, целиком закрытая предоплатой,
    /// не двигает max и не добавляет ничего. МИНУС тоже законный: кредит-нота
    /// уменьшает Shipped, цель опускается ниже обложенного, и налог должен
    /// освободиться.
    /// </summary>
    public async Task<decimal> TaxableIncrementAsync(
        Guid legalEntity, Guid customer, Guid contract, decimal rate)
    {
        if (contract == Guid.Empty) return 0m;

        var shipped = await BalanceAsync(legalEntity, customer, contract, "Shipped");
        var paidGross = await BalanceAsync(legalEntity, customer, contract, "Paid");
        var accrued = await BalanceAsync(legalEntity, customer, contract, "Accrued");

        // ЗНАКОВАЯ величина, и это существенно. Вверх её двигают отгрузка и
        // оплата, вниз — кредит-нота, уменьшающая Shipped. Зажми минус в ноль,
        // как было сначала, и налог по кредит-ноте не освободится, а планка
        // max(Shipped, Paid) останется завышенной: следующая отгрузка по
        // договору не начислит ничего, пока её не перекроет.
        return Math.Max(shipped, Net(paidGross, rate)) - accrued;
    }

    /// <summary>
    /// База единого налога, ещё не обложенная: прирост ПОЛУЧЕННЫХ ДЕНЕГ над
    /// EpAccrued. Ни отгрузка, ни кредит-нота сюда не попадают — ЄП кассовый,
    /// его рождает только приход денег.
    ///
    /// vatRate ≠ 0 ТОЛЬКО У СПРОЩЕНЦЯ 3%: он плательщик ПДВ, и единый налог
    /// берётся с дохода БЕЗ ПДВ — 120 полученных при ставке 20% дают базу ЄП 100.
    /// У пятипроцентника ПДВ в деньгах нет вовсе, база — вся полученная сумма,
    /// и vatRate сюда приходит нулём.
    /// </summary>
    public async Task<decimal> SingleTaxIncrementAsync(
        Guid legalEntity, Guid customer, Guid contract, decimal vatRate)
    {
        if (contract == Guid.Empty) return 0m;

        var paidGross = await BalanceAsync(legalEntity, customer, contract, "Paid");
        var taxed = await BalanceAsync(legalEntity, customer, contract, "EpAccrued");

        return Net(paidGross, vatRate) - taxed;
    }

    /// <summary>Деньги с налогом → база без него. Ставка 0 — возвращает как есть.</summary>
    private static decimal Net(decimal gross, decimal rate)
    {
        if (rate <= 0m) return gross;
        var scale = GlobalConstants.Get<int?>("AmountScale") ?? 2;
        return Math.Round(gross / (1m + rate), scale, MidpointRounding.AwayFromZero);
    }

    // ═══ ГОДОВОЙ ПРЕДЕЛ ДОХОДА (ПКУ 291.4 / 293.4) ═══════════════════════════
    //
    // Превышение предела НЕ ЗАПРЕЩЕНО, и документ из-за него не отклоняется:
    // деньги пришли, отказывать в доходе не за что. Закон говорит другое — сумма
    // сверх предела облагается по 15%, а с начала следующего квартала спрощенець
    // переходит на общую систему. Здесь сделана первая половина, денежная;
    // перевод режима — отдельная работа, он к ставке не сводится.

    /// <summary>
    /// Предел, действующий на дату. Ноль означает «не задан», и тогда превышение
    /// не считается вовсе: весь доход идёт по обычной ставке, ровно как до
    /// появления этого справочника.
    ///
    /// Окно выбирается В ПАМЯТИ, а не фильтром SQL: строк тут единицы, а датовый
    /// литерал в строке фильтра зависел бы от диалекта (стенд живёт и на SQL
    /// Server, и на Postgres) и от языковых настроек сервера. Той же
    /// осторожностью живёт разрешение ставок в ITaxService.
    /// </summary>
    public async Task<decimal> SingleTaxLimitOnAsync(DateTime onDate)
    {
        var day = onDate.Date;
        var live = (await _limits.GetRecordsAsync("1 = 1"))
            .Where(r => r.EffectiveFrom.Date <= day && (r.EffectiveTo?.Date ?? DateTime.MaxValue) >= day)
            .OrderByDescending(r => r.EffectiveFrom)
            .ToList();
        return live.Count > 0 ? live[0].Amount : 0m;
    }

    /// <summary>
    /// Сколько базы единого налога юрлицо уже набрало В ЭТОМ КАЛЕНДАРНОМ ГОДУ.
    ///
    /// Считается по ДВИЖЕНИЯМ EpAccrued, а не по остатку: остаток копится за всю
    /// историю, а предел — годовой. Зовётся ДО записи текущего прироста, поэтому
    /// отвечает «сколько было до этого события».
    ///
    /// Юрлицо отбирается фильтром (это измерение, то есть обычная колонка), а год
    /// — в памяти, по той же причине, что и окно предела выше.
    /// </summary>
    public async Task<decimal> SingleTaxBaseInYearAsync(Guid legalEntity, DateTime onDate)
    {
        var movements = await _totals.QueryMovementsAsync(
            "UaVatFirstEvent", $"[LegalEntity] = '{legalEntity}'");

        var used = 0m;
        foreach (var m in movements)
        {
            if (!m.TryGetValue("MovementDate", out var raw) || raw is null) continue;
            if (!DateTime.TryParse(Convert.ToString(raw), out var when) || when.Year != onDate.Year) continue;
            if (!m.TryGetValue("EpAccrued", out var value) || value is null) continue;
            used += Convert.ToDecimal(value);
        }
        return used;
    }

    // ═══ ПРИНУДИТЕЛЬНЫЙ ПЕРЕХОД НА ОБЩУЮ СИСТЕМУ (ПКУ 293.8) ═════════════════
    //
    // Превысил предел — с первого числа месяца, СЛЕДУЮЩЕГО ЗА КВАРТАЛОМ
    // превышения, спрощенець переходит на общую систему. Не с даты превышения и
    // не со следующего месяца: именно с квартальной границы.
    //
    // ПОЧЕМУ ЖУРНАЛ, А НЕ ПРОСТО ПОЛЕ НА ЮРЛИЦЕ. Переход решается СЕГОДНЯ, а
    // случается через недели. Между этими двумя моментами кто-то спросит, почему
    // ФОП перестал быть спрощенцем — и ответ обязан быть в системе, с датой
    // превышения и датой перехода, а не выводиться заново из движений.

    /// <summary>
    /// Первое число месяца, следующего за кварталом даты. Для 5 мая (II квартал,
    /// апрель–июнь) это 1 июля; для 20 декабря — 1 января следующего года.
    /// </summary>
    public DateTime NextQuarterStart(DateTime after)
    {
        var quarter = (after.Month - 1) / 3;            // 0..3
        var firstMonthOfNext = quarter * 3 + 4;         // 4, 7, 10, 13
        return firstMonthOfNext > 12
            ? new DateTime(after.Year + 1, 1, 1)
            : new DateTime(after.Year, firstMonthOfNext, 1);
    }

    /// <summary>Куда переводит превышение. Плательщик ПДВ им и остаётся.</summary>
    public UaTaxRegime ForcedTargetOf(UaTaxRegime regime)
        => ChargesVat(regime) ? UaTaxRegime.VatPayer : UaTaxRegime.General;

    /// <summary>
    /// Записать, что юрлицу предстоит переход. ИДЕМПОТЕНТНО: драйвер итогов может
    /// отработать по одному документу не один раз (перепроведение, повторный хук),
    /// и три строки об одном и том же переходе — это не аудит, а мусор. Ключ
    /// неприменённой строки — юрлицо плюс дата перехода.
    /// </summary>
    public async Task<bool> ScheduleForcedChangeAsync(
        Guid legalEntity, UaTaxRegime regime, DateTime exceededOn)
    {
        if (legalEntity == Guid.Empty) return false;
        var target = ForcedTargetOf(regime);
        if (target == regime) return false;             // переводить некуда

        var effective = NextQuarterStart(exceededOn);
        var existing = await _changes.GetRecordsAsync($"LegalEntity = '{legalEntity}'");
        if (existing.Any(r => r.EffectiveFrom.Date == effective.Date)) return false;

        var row = _dictionaries.NewRecord<UaRegimeChange>();
        row.LegalEntity = legalEntity;
        row.FromRegime = regime;
        row.ToRegime = target;
        row.ExceededOn = exceededOn.Date;
        row.EffectiveFrom = effective;
        await _dictionaries.SaveRecordAsync(row);
        return true;
    }

    /// <summary>
    /// Применить все переходы, чья дата уже наступила. Возвращает, сколько юрлиц
    /// переведено. Зовётся заданием: дата перехода лежит в будущем, и в момент
    /// превышения сделать нечего.
    ///
    /// AppliedOn — и отметка в аудите, и предохранитель: повторный запуск задания
    /// в тот же день ничего не переставит второй раз.
    /// </summary>
    public async Task<int> ApplyDueRegimeChangesAsync(DateTime onDate)
    {
        var day = onDate.Date;
        var due = (await _changes.GetRecordsAsync("1 = 1"))
            .Where(r => r.AppliedOn is null && r.EffectiveFrom.Date <= day)
            .OrderBy(r => r.EffectiveFrom)
            .ToList();

        var applied = 0;
        foreach (var row in due)
        {
            // Режим — поле РАСШИРЕНИЯ чужого справочника: в типизированный класс
            // LegalEntity оно не попадает, пишется мешком и ЧИСЛОМ (через
            // IDataService разбора имён перечисления нет).
            var entity = await _data.GetByIdAsync("LegalEntity", row.LegalEntity);
            if (entity is null) continue;

            await _data.UpdateAsync("LegalEntity", row.LegalEntity,
                new Dictionary<string, object?>
                {
                    ["UaTaxRegime"] = (int)row.ToRegime,
                    ["Country"] = entity["Country"],
                    ["Currency"] = entity["Currency"],
                });

            // UpdateAsync может не поднять OnAfterSave юрлица — сопоставление
            // с кодом освобождения тогда осталось бы от прошлого режима.
            await SyncExemptMappingAsync(row.LegalEntity);

            row.AppliedOn = day;
            await _dictionaries.SaveRecordAsync(row);
            applied++;
        }
        return applied;
    }

    /// <summary>
    /// Код освобождения от ПДВ, уже разрешённый в запись справочника. null —
    /// в настройках пусто или такого TaxCode нет; тогда сопоставление не
    /// заводится, и общий путь Sales → Tax берёт DefaultTaxCode.
    /// </summary>
    public async Task<Guid?> ExemptVatCodeIdAsync()
    {
        var settings = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        var code = settings?.ExemptVatCode;
        if (string.IsNullOrWhiteSpace(code)) return null;
        return (await _codes.GetRecordsAsync($"Code = '{code.Replace("'", "''")}'"))
            .FirstOrDefault()?.MetaId;
    }

    /// <summary>
    /// Штатное сопоставление «это юрлицо не платит ПДВ». Общий расчёт налога
    /// (Sales → Tax) про украинский режим не знает и без этой строки берёт
    /// DefaultTaxCode: в леджере у спрощенця 5% оказался бы ПДВ, которого нет
    /// в UaVatPayable.
    ///
    /// НЕ ТРОГАЕТ чужие сопоставления на том же юрлице: пересечение окон
    /// одного источника отклоняется, и затирать ручную строку нельзя. Нет кода
    /// в настройках — ничего не делает, как и єдиний податок без своего кода.
    /// </summary>
    public async Task SyncExemptMappingAsync(Guid legalEntity)
    {
        if (legalEntity == Guid.Empty) return;

        var exemptId = await ExemptVatCodeIdAsync();
        if (exemptId is null) return;

        var regime = await RegimeOfAsync(legalEntity);
        var rows = (await _mappings.GetRecordsAsync(
                $"SourceType = 'LegalEntity' AND SourceId = '{legalEntity}'"))
            .ToList();
        var live = rows.Where(m => !m.IsDisabled).ToList();
        var ours = live.Where(m => m.TaxCode == exemptId.Value).ToList();

        if (ChargesVat(regime))
        {
            foreach (var mapping in ours)
            {
                mapping.IsDisabled = true;
                await _dictionaries.SaveRecordAsync(mapping);
            }
            return;
        }

        if (ours.Count > 0) return;

        // Чужое живое сопоставление на этом юрлице — не заводим второе: окна
        // одного источника пересекаться не могут, а затирать чужой код нельзя.
        if (live.Count > 0) return;

        var closed = rows
            .Where(m => m.TaxCode == exemptId.Value)
            .OrderByDescending(m => m.EffectiveFrom)
            .FirstOrDefault();
        if (closed is not null)
        {
            closed.IsDisabled = false;
            closed.EffectiveTo = null;
            await _dictionaries.SaveRecordAsync(closed);
            return;
        }

        var created = _dictionaries.NewRecord<TaxMapping>();
        created.SourceType = "LegalEntity";
        created.SourceId = legalEntity;
        created.TaxCode = exemptId.Value;
        created.Priority = 0;
        created.EffectiveFrom = DateTime.UtcNow.Date;
        await _dictionaries.SaveRecordAsync(created);
    }

    /// <summary>
    /// Код ставки 15% на превышение, уже разрешённый в запись справочника.
    /// null — код в настройках не заполнен или такого TaxCode нет; тогда
    /// превышение просто не выделяется отдельной строкой.
    /// </summary>
    public async Task<Guid?> SingleTaxExcessCodeIdAsync()
    {
        var settings = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        var code = settings?.SingleTaxCodeExcess;
        if (string.IsNullOrWhiteSpace(code)) return null;
        return (await _codes.GetRecordsAsync($"Code = '{code}'")).FirstOrDefault()?.MetaId;
    }

    /// <summary>
    /// Сколько из прироста укладывается в предел. Остаток прироста — превышение,
    /// и его вызывающий облагает по своей ставке. Предел не задан (ноль) — всё
    /// уходит в обычную часть, поведение не отличается от прежнего.
    /// </summary>
    public decimal SingleTaxWithinLimit(decimal increment, decimal used, decimal limit)
    {
        if (limit <= 0m) return increment;
        var room = limit - used;
        if (room <= 0m) return 0m;
        return room >= increment ? increment : room;
    }

    // ═══ ВХОД: ПОДАТКОВИЙ КРЕДИТ (ПКУ 198.2) ═════════════════════════════════
    //
    // Зеркало продаж, и правило то же по форме: кредит возникает на дату того из
    // событий, что случилось РАНЬШЕ — списание денег поставщику или получение
    // товара. Поэтому и считается так же: копятся два итога, зачитывается max из
    // них, Credited помнит зачтённое, каждое событие берёт только прирост.
    //
    // РАЗНИЦА ОДНА, И ОНА В КЛЮЧЕ: договоров поставки в Purchasing нет, поэтому
    // ключ — юрлицо и поставщик, без третьего измерения. Следствие, которое надо
    // знать: все закупки у одного поставщика одним юрлицом идут в ОДНУ корзину,
    // и первое событие выводится по ней целиком, а не по сделке.

    /// <summary>
    /// Какая часть базы поставщика даёт налоговый кредит ПРЯМО СЕЙЧАС: прирост
    /// max(Received, PaidOut) над уже зачтённым. Зовётся ПОСЛЕ записи движений.
    ///
    /// Асимметрия та же, что на продажах: Received — база БЕЗ налога, PaidOut —
    /// деньги С налогом. Аванс 120 при ставке 20% закрывает базу 100, а не 120.
    ///
    /// Минус законен: возврат поставщику уменьшает Received, цель опускается ниже
    /// зачтённого, и кредит обязан сняться.
    /// </summary>
    public async Task<decimal> CreditIncrementAsync(Guid legalEntity, Guid supplier, decimal rate)
    {
        if (supplier == Guid.Empty) return 0m;

        var received = await PurchaseBalanceAsync(legalEntity, supplier, "Received");
        var paidGross = await PurchaseBalanceAsync(legalEntity, supplier, "PaidOut");
        var credited = await PurchaseBalanceAsync(legalEntity, supplier, "Credited");

        return Math.Max(received, Net(paidGross, rate)) - credited;
    }

    private Task<decimal> PurchaseBalanceAsync(Guid legalEntity, Guid supplier, string resource)
        => _totals.GetBalanceAsync("UaPurchaseFirstEvent", resource,
            new Dictionary<string, object?>
            {
                ["LegalEntity"] = legalEntity,
                ["Supplier"] = supplier,
            });

    private Task<decimal> BalanceAsync(
        Guid legalEntity, Guid customer, Guid contract, string resource)
        => _totals.GetBalanceAsync("UaVatFirstEvent", resource,
            new Dictionary<string, object?>
            {
                ["LegalEntity"] = legalEntity,
                ["Customer"] = customer,
                ["SalesContract"] = contract,
            });
}
