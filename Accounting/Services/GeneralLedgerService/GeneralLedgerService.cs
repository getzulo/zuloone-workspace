using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Сервис "GeneralLedgerService": контракт IGeneralLedgerService. Единая точка
// разноски субледжеров (продажи, закупки, ФОТ) в главную книгу. Подсистема
// решает ЧТО и НА КАКИЕ счета разнести, вся механика проводки — здесь.
//
// Счета берутся из ПРОФИЛЯ — одиночного справочника AccountingSettings (форма
// настроек модуля), а не из глобальных констант: план счетов настраивается в UI.
//
// ВАЖНО про DI: зависимости резолвятся ЛЕНИВО из scope, а НЕ через конструктор.
// Платформенные менеджеры берут контекст через IDbContextFactory — каждый создаёт
// СВОЁ соединение, и конструктор, разрешающий их пачкой внутри уже открытой
// транзакции проведения, заставляет её повышаться до распределённой (в контейнере
// нет MSDTC → "Failure while attempting to promote transaction"). Ленивый резолв
// повторяет поведение инлайн-кода: по одному менеджеру за раз.
//
// Разноска BEST-EFFORT: нет счетов/периода/юрлица → null, вызывающий тихо пропускает.
public partial class GeneralLedgerService
{
    private static readonly Guid JournalEntryType = Guid.Parse("188246b3-5ed0-4da0-98cb-a86b6da36581");

    // Менеджеры инжектятся обычным образом. Захватывать IServiceProvider и
    // резолвить из него лениво НЕЛЬЗЯ: сервис живёт дольше своего scope, и к
    // моменту события after-post (оно выполняется уже после закрытия области)
    // такой провайдер выдаёт ObjectDisposedException — разноска молча пропадала.
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

    /// <summary>Профиль настроек учёта (одна запись); null, если ещё не заполнен.</summary>
    public async Task<AccountingSettings?> GetSettingsAsync()
        => (await _settings.GetRecordsAsync()).FirstOrDefault();

    /// <summary>Счёт плана счетов по коду; null, если код пуст или счёт не найден.</summary>
    public async Task<Guid?> ResolveAccountAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var accounts = _accounts;
        return (await accounts.GetRecordsAsync($"Code = '{code}'")).FirstOrDefault()?.MetaId;
    }

    /// <summary>
    /// Причина, по которой код счёта НЕ годится для проводок, — или null, если
    /// годится.
    ///
    /// ЧТО СЧИТАЕТСЯ ПРОБЛЕМОЙ, А ЧТО НЕТ. Пустой код и код НЕСУЩЕСТВУЮЩЕГО
    /// счёта означают одно: «эта нога ещё не настроена». Это законное состояние —
    /// профиль заполняют до того, как достроен план счетов, а разноска такую
    /// ногу тихо пропускает, как и раньше. Ругаться на это значит запретить
    /// сохранить профиль, пока не заведён каждый счёт из двенадцати.
    ///
    /// Проблема — код СУЩЕСТВУЮЩЕГО счёта, который помечен НЕпроводимым. Это уже
    /// не «не настроено», а настроено НЕВЕРНО: поле выглядит заполненным, счёт в
    /// плане есть, а проводки на него не будет никогда. Именно этот случай надо
    /// поймать там, где человек видит поле.
    ///
    /// ОДНО ПРАВИЛО НА ДВЕ ДВЕРИ: сохранение профиля настроек и сама разноска
    /// (ResolvePairAsync отсеивает непроводимые счета тем же признаком).
    /// Проводки принимают только ЛИСТЬЯ: у счёта-группы собственный остаток
    /// обязан быть суммой подчинённых, и прямая проводка на него её ломает.
    /// Ставить IsPostable группе не даёт ChartOfAccountsEventHandler.
    /// </summary>
    public async Task<string?> AccountCodeProblemAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var account = (await _accounts.GetRecordsAsync($"Code = '{code}'")).FirstOrDefault();
        if (account is null) return null;   // счёта ещё нет — «не настроено», не ошибка
        if (!account.IsPostable)
            return $"счёт «{code}» ({account.Name}) не проводимый — это группа, "
                 + "проводки принимают только конечные счета";
        return null;
    }

    /// <summary>Пара счетов ОДНИМ запросом. Каждый вызов менеджера — это два
    /// обращения к БД (метаданные таблицы + данные), а каждое берёт соединение из
    /// пула и вступает в транзакцию проведения; лишние round-trip'ы толкают её к
    /// повышению до распределённой. Поэтому дебет и кредит ищутся вместе.</summary>
    private async Task<(Guid? Debit, Guid? Credit)> ResolvePairAsync(string debitCode, string creditCode)
    {
        if (string.IsNullOrWhiteSpace(debitCode) || string.IsNullOrWhiteSpace(creditCode)) return (null, null);
        var accounts = _accounts;
        var found = await accounts.GetRecordsAsync($"Code = '{debitCode}' OR Code = '{creditCode}'");
        // Непроводимый счёт (группа) отсеивается здесь же: для вызывающего это
        // неотличимо от «счёта нет», и это верно — разносить не на что в обоих
        // случаях. Настройку с таким кодом ловит обработчик профиля, где отказ
        // виден человеку.
        return (found.FirstOrDefault(a => a.Code == debitCode && a.IsPostable)?.MetaId,
                found.FirstOrDefault(a => a.Code == creditCode && a.IsPostable)?.MetaId);
    }

    /// <summary>Учётный период, покрывающий дату; null, если такого периода нет.</summary>
    public async Task<Guid?> ResolvePeriodAsync(DateTime date)
        => (await ResolvePeriodRecordAsync(date))?.MetaId;

    /// <summary>
    /// Запись учётного периода, покрывающего дату. НЕСКОЛЬКО подходящих периодов —
    /// порча мастер-данных: отчётность за месяц зависела бы от порядка строк в
    /// справочнике. Это не разрешается молча «взять первый» — отказ называет оба
    /// периода, чтобы настройку можно было починить (ровно как TaxService
    /// поступает с пересекающимися ставками).
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

    /// <summary>Признак закрытого периода. Статус — строка (закрытый набор, который
    /// метаданными пока не выражен), поэтому сравнение регистронезависимое и по
    /// принципу «всё, что не Open, — закрыто»: опечатка в статусе обязана
    /// ЗАПРЕЩАТЬ проводку, а не разрешать её.</summary>
    private static bool IsPeriodOpen(FiscalPeriod period)
        => string.Equals(period.Status, "Open", StringComparison.OrdinalIgnoreCase);

    /// <summary>Разнести сбалансированную проводку Dr/Cr по КОДАМ счетов из профиля.
    /// Возвращает id проводки или null, если разноска невозможна ИЛИ этот факт уже
    /// разнесён.
    ///
    /// Контуры: торговые проводки по умолчанию пишут финансовую и управленческую
    /// книги (FIN,MGT). Налоговые ноги передают «FIN,TAX», чтобы управленческая
    /// книга оставалась нетто без НДС — налог живёт отдельной проводкой в FIN+TAX.
    /// </summary>
    public async Task<Guid?> PostAsync(
        DateTime date, Guid legalEntity, Guid currency, decimal amount,
        string debitAccountCode, string creditAccountCode,
        string description, string debitLineText, string creditLineText,
        string? circuits = null)
    {
        if (amount <= 0m || legalEntity == Guid.Empty) return null;

        // ОДНО ОПИСАНИЕ — ОДНА ПРОВОДКА. Описание несёт id документа-источника и
        // назначение ("Sales invoice <id>", "Cost of sales <id>", "Purchase order
        // <id>"), поэтому повтор означает повторную разноску ТОГО ЖЕ факта, а не
        // второй факт.
        //
        // Защита не теоретическая: событие after-post документа выполняется ДВАЖДЫ,
        // когда его же проведение дописывает движения через менеджер — так делает
        // драйвер CostingIssue, списывая себестоимость проданного. Без этой
        // проверки любая продажа товара, у которого есть слои себестоимости,
        // удваивала в книге и выручку, и себестоимость (поймано тестом
        // CostOfSalesGLTest; старый SalesGLPostingTest этого не видел, потому что
        // заводит остаток прямым движением регистра — списывать нечего, и события
        // хватало одного).
        //
        // Отмена и перепроведение документа сюда тоже приходят: и там повтор
        // блокировать ПРАВИЛЬНО — первую проводку никто не сторнировал, она
        // осталась в книге.
        var alreadyPosted = await _documents.CountDocumentsAsync<JournalEntry>(
            $"Description = '{description.Replace("'", "''")}'");
        if (alreadyPosted > 0) return null;

        var (debit, credit) = await ResolvePairAsync(debitAccountCode, creditAccountCode);
        if (debit == null || credit == null) return null;

        var period = await ResolvePeriodRecordAsync(date);
        if (period == null) return null;

        // ЗАКРЫТЫЙ ПЕРИОД НЕ ПРИНИМАЕТ НОВЫХ ФАКТОВ. Проверка появилась вместе с
        // тем, что проводка стала датироваться датой ДОКУМЕНТА: до этого попасть
        // в прошлый месяц было нельзя вовсе, а теперь можно — и закрытый месяц
        // надо защищать явно.
        //
        // ГРАНИЦА ЗДЕСЬ НЕ ЕДИНСТВЕННАЯ И НЕ САМАЯ СИЛЬНАЯ. У платформы есть своя,
        // глобальная (IAccountingPeriodService.ClosedPeriod): её проверяет
        // DocumentPostingService на КАЖДОМ проведении, то есть она держит и те
        // документы, у которых ноги в главную книгу нет вовсе. Выставляется она
        // оператором через /api/accounting-periods — под именованным правом и с
        // записью в аудит, — и автоматически из статуса периода НЕ выводится:
        // одна дата не выражает «февраль закрыт, январь открыт», а право на её
        // сдвиг умышленно отделено от права редактировать справочник.
        if (!IsPeriodOpen(period)) return null;

        // Проводка создаётся типизированным менеджером документов: он сам выдаёт
        // MetaId и номер из нумератора, исполняет OnBeforeCreate/OnBeforeInsert и
        // валидацию обязательных полей, а строки пишет из табличной части.
        // Перевод в «Проведено» исполняет GLPostingTx → движения по регистру GL.
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
