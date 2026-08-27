using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
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

    /// <summary>Пара счетов ОДНИМ запросом. Каждый вызов менеджера — это два
    /// обращения к БД (метаданные таблицы + данные), а каждое берёт соединение из
    /// пула и вступает в транзакцию проведения; лишние round-trip'ы толкают её к
    /// повышению до распределённой. Поэтому дебет и кредит ищутся вместе.</summary>
    private async Task<(Guid? Debit, Guid? Credit)> ResolvePairAsync(string debitCode, string creditCode)
    {
        if (string.IsNullOrWhiteSpace(debitCode) || string.IsNullOrWhiteSpace(creditCode)) return (null, null);
        var accounts = _accounts;
        var found = await accounts.GetRecordsAsync($"Code = '{debitCode}' OR Code = '{creditCode}'");
        return (found.FirstOrDefault(a => a.Code == debitCode)?.MetaId,
                found.FirstOrDefault(a => a.Code == creditCode)?.MetaId);
    }

    /// <summary>Учётный период, покрывающий дату; null, если такого периода нет.</summary>
    public async Task<Guid?> ResolvePeriodAsync(DateTime date)
    {
        var d = date.Date;
        var periods = _periods;
        return (await periods.GetRecordsAsync())
            .FirstOrDefault(p => d >= p.FromDate.Date && d <= p.ToDate.Date)?.MetaId;
    }

    /// <summary>Разнести сбалансированную проводку Dr/Cr по КОДАМ счетов из профиля.
    /// Возвращает id проводки или null, если разноска невозможна.</summary>
    public async Task<Guid?> PostAsync(
        DateTime date, Guid legalEntity, Guid currency, decimal amount,
        string debitAccountCode, string creditAccountCode,
        string description, string debitLineText, string creditLineText)
    {
        if (amount <= 0m || legalEntity == Guid.Empty) return null;

        var (debit, credit) = await ResolvePairAsync(debitAccountCode, creditAccountCode);
        if (debit == null || credit == null) return null;

        var period = await ResolvePeriodAsync(date);
        if (period == null) return null;

        // Проводка создаётся типизированным менеджером документов: он сам выдаёт
        // MetaId и номер из нумератора, исполняет OnBeforeCreate/OnBeforeInsert и
        // валидацию обязательных полей, а строки пишет из табличной части.
        // Перевод в «Проведено» исполняет GLPostingTx → движения по регистру GL.
        var documents = _documents;
        var entry = await documents.NewDocumentAsync<JournalEntry>("Draft", new Dictionary<string, object?>
        {
            ["DocumentDate"] = date.Date,
            ["LegalEntity"] = legalEntity,
            ["FiscalPeriod"] = period.Value,
            ["Currency"] = currency,
            ["Description"] = description,
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
