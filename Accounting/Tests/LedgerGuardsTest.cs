using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;
// Сгенерированные классы сущностей. Тест-скриптам этот namespace НЕ приходит
// глобальным using'ом.
using ZuloOne.Runtime.Generated;

// ЦЕЛОСТНОСТЬ УЧЁТНОГО КОНТУРА: период и счёт, на которые ложится проводка.
//
// Проводка выбирает учётный период ПО ДАТЕ (GeneralLedgerService.ResolvePeriodAsync)
// и счёт ПО КОДУ из профиля настроек. Оба выбора до сих пор были беззащитны:
//
//  1. Периоды никто не проверял на пересечение, а подбор берёт FirstOrDefault —
//     то есть на дату, попавшую в два периода, проводка уходит в СЛУЧАЙНЫЙ из
//     них, и отчётность за месяц зависит от порядка строк в справочнике.
//  2. Статус периода не читала ни одна строка кода: «закрытый» месяц принимал
//     проводки молча.
//  3. План счетов различает ЛИСТЬЯ и ГРУППЫ (IsPostable), но при разноске это
//     не проверялось — проводка могла лечь на группу, и её собственный остаток
//     разъехался бы с суммой детей.
//
// Каждый кейс проверяет ТЕКСТ отказа либо конкретный отказ разноски, а не факт
// исключения: голый catch зеленел бы от любой поломки в расстановке данных.
public class LedgerGuardsTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IGeneralLedgerService Gl => GetService<IGeneralLedgerService>();

    private Guid _currency;
    private Guid _legalEntity;

    /// <summary>Валюта, страна и юрлицо — минимум, без которого проводку не собрать.</summary>
    private async Task PartyAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = "EUR";
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);
        _currency = currency.MetaId;

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var le = DictionaryManager.NewRecord<LegalEntity>();
        le.Name = "ACME GmbH";
        le.RegistrationNumber = $"REG-LG-{Db.NewId():N}"[..16];
        le.Country = country.MetaId;
        le.Currency = _currency;
        _legalEntity = (await DictionaryManager.SaveRecordAsync(le)).MetaId;
    }

    private async Task<Guid> YearAsync(int year)
    {
        var fy = DictionaryManager.NewRecord<FiscalYear>();
        fy.Code = $"FY{year}-{Db.NewId():N}"[..10];
        fy.StartDate = new DateTime(year, 1, 1);
        fy.EndDate = new DateTime(year, 12, 31);
        fy.IsClosed = false;
        return (await DictionaryManager.SaveRecordAsync(fy)).MetaId;
    }

    private async Task<FiscalPeriod> PeriodAsync(
        Guid year, string code, DateTime from, DateTime to, string status = "Open")
    {
        var p = DictionaryManager.NewRecord<FiscalPeriod>();
        p.Code = $"{code}-{Db.NewId():N}"[..10];
        p.FiscalYear = year;
        p.FromDate = from;
        p.ToDate = to;
        p.Status = status;
        return await DictionaryManager.SaveRecordAsync(p);
    }

    private async Task<Guid> AccountAsync(string code, string name, AccountType type, bool postable = true)
    {
        var a = DictionaryManager.NewRecord<ChartOfAccounts>();
        a.Code = code;
        a.Name = name;
        a.AccountType = type;
        a.IsPostable = postable;
        a.Currency = _currency;
        return (await DictionaryManager.SaveRecordAsync(a)).MetaId;
    }

    /// <summary>Профиль настроек — ОДИНОЧНЫЙ и КЭШИРУЕМЫЙ: кэш переживает откат
    /// кейса, поэтому правим существующую запись, а не заводим слепо.</summary>
    private async Task SettingsAsync(string debitCode, string creditCode)
    {
        var rows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        var s = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<AccountingSettings>();
        s.ArAccountCode = debitCode;
        s.RevenueAccountCode = creditCode;
        await DictionaryManager.SaveRecordAsync(s);
    }

    [IntegrationTest("Пересекающиеся учётные периоды отклоняются при вводе")]
    public async Task OverlappingPeriodsAreRejected()
    {
        await PartyAsync();
        var year = await YearAsync(2026);
        await PeriodAsync(year, "P01", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        var overlapping = DictionaryManager.NewRecord<FiscalPeriod>();
        overlapping.Code = $"P01b-{Db.NewId():N}"[..10];
        overlapping.FiscalYear = year;
        overlapping.FromDate = new DateTime(2026, 1, 15);   // внутрь уже занятого окна
        overlapping.ToDate = new DateTime(2026, 2, 15);
        overlapping.Status = "Open";

        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(overlapping); }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Contains("пересекается"),
            "период, перекрывающий существующий, обязан быть отклонён: на дату должен приходиться "
            + "ровно один период, иначе проводка уходит в случайный. Факт: {0}", reason);
    }

    [IntegrationTest("Смежные периоды принимаются: конец одного и начало следующего")]
    public async Task AdjacentPeriodsAreAccepted()
    {
        // Обратная сторона проверки: она не должна мешать нормальному календарю.
        // Без этого кейса «пересечением» можно было бы объявить что угодно.
        await PartyAsync();
        var year = await YearAsync(2026);
        await PeriodAsync(year, "P01", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        var february = await PeriodAsync(year, "P02", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        Assert.IsTrue(february.MetaId != Guid.Empty,
            "период, начинающийся назавтра после конца предыдущего, обязан заводиться");
    }

    [IntegrationTest("Закрыть период, пока открыт более ранний, нельзя")]
    public async Task ClosingOutOfOrderIsRejected()
    {
        // Учётные периоды закрываются ПО ПОРЯДКУ. Разрешить закрыть февраль при
        // открытом январе — значит получить дырявую границу: платформенный запрет
        // проведения выражается ОДНОЙ датой, и «закрыт февраль, открыт январь»
        // в неё не отображается.
        await PartyAsync();
        var year = await YearAsync(2026);
        await PeriodAsync(year, "P01", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        var february = await PeriodAsync(year, "P02", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        february.Status = "Closed";
        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(february); }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Contains("более ранний"),
            "закрытие периода через голову открытого предыдущего обязано быть отклонено, факт: {0}", reason);
    }

    [IntegrationTest("Закрытие по порядку проходит")]
    public async Task ClosingInOrderIsAccepted()
    {
        await PartyAsync();
        var year = await YearAsync(2026);
        var january = await PeriodAsync(year, "P01", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        var february = await PeriodAsync(year, "P02", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        january.Status = "Closed";
        january = await DictionaryManager.SaveRecordAsync(january);
        february.Status = "Closed";
        february = await DictionaryManager.SaveRecordAsync(february);

        Assert.IsTrue(february.Status == "Closed",
            "после закрытия января февраль обязан закрываться, факт статус {0}", february.Status);
    }

    [IntegrationTest("Проводка в закрытый период не разносится")]
    public async Task PostingIntoClosedPeriodIsRefused()
    {
        await PartyAsync();
        var year = await YearAsync(2026);
        var january = await PeriodAsync(year, "P01", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        await AccountAsync("1200", "Receivables", AccountType.Asset);
        await AccountAsync("4000", "Revenue", AccountType.Income);
        await SettingsAsync("1200", "4000");

        // Сначала убеждаемся, что в ОТКРЫТЫЙ период та же проводка проходит:
        // иначе отказ ниже нельзя отличить от неверной расстановки данных.
        var open = await Gl.PostAsync(
            new DateTime(2026, 1, 15), _legalEntity, _currency, 100m, "1200", "4000",
            $"Open period probe {Db.NewId():N}", "Дебет", "Кредит");
        Assert.IsTrue(open.HasValue, "в открытый период проводка обязана проходить");

        january.Status = "Closed";
        await DictionaryManager.SaveRecordAsync(january);

        var closed = await Gl.PostAsync(
            new DateTime(2026, 1, 20), _legalEntity, _currency, 100m, "1200", "4000",
            $"Closed period probe {Db.NewId():N}", "Дебет", "Кредит");
        Assert.IsNull(closed,
            "проводка датой внутри ЗАКРЫТОГО периода разноситься не должна");
    }

    [IntegrationTest("Проводка на счёт-группу не разносится")]
    public async Task PostingToNonPostableAccountIsRefused()
    {
        // Профиль настроек с таким кодом теперь не сохранить (см. кейс ниже),
        // поэтому сюда код передаётся ПРЯМО в PostAsync — а это и есть тот
        // случай, ради которого рубеж на разноске оставлен: код счёта пришёл в
        // обход формы настроек (импорт, миграция, вызов из чужого скрипта).
        await PartyAsync();
        var year = await YearAsync(2026);
        await PeriodAsync(year, "P01", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        await AccountAsync("1200", "Receivables", AccountType.Asset);
        await AccountAsync("4000", "Revenue group", AccountType.Income, postable: false);

        // Контроль: на паре ПРОВОДИМЫХ счетов та же проводка проходит — значит
        // отказ ниже вызван именно непроводимостью, а не расстановкой данных.
        await AccountAsync("4001", "Revenue", AccountType.Income);
        var ok = await Gl.PostAsync(
            new DateTime(2026, 1, 15), _legalEntity, _currency, 100m, "1200", "4001",
            $"Postable probe {Db.NewId():N}", "Дебет", "Кредит");
        Assert.IsTrue(ok.HasValue, "на паре проводимых счетов проводка обязана проходить");

        var posted = await Gl.PostAsync(
            new DateTime(2026, 1, 15), _legalEntity, _currency, 100m, "1200", "4000",
            $"Group account probe {Db.NewId():N}", "Дебет", "Кредит");

        Assert.IsNull(posted,
            "счёт, помеченный НЕпроводимым (группа), не должен принимать проводку: "
            + "иначе его остаток разъезжается с суммой подчинённых счетов");
    }

    [IntegrationTest("Код счёта в настройках обязан указывать на проводимый счёт")]
    public async Task SettingsRejectGroupAccountCode()
    {
        // Отказ на РАЗНОСКЕ — последний рубеж, и он тихий (разноска best-effort).
        // Поймать ошибку настройки надо там, где её допускают: при сохранении
        // профиля счетов, где человек видит поле и может его исправить.
        await PartyAsync();
        await AccountAsync("1200", "Receivables", AccountType.Asset);
        await AccountAsync("4000", "Revenue group", AccountType.Income, postable: false);

        var rows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        var s = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<AccountingSettings>();
        s.ArAccountCode = "1200";
        s.RevenueAccountCode = "4000";   // группа

        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(s); }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Contains("не проводимый") || reason.Contains("группа"),
            "профиль с кодом счёта-группы обязан быть отклонён с внятной причиной, факт: {0}", reason);
    }
}
