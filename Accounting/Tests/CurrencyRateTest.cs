using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Тестовый скрипт НЕ получает global usings — пространства имён названы явно.
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// КУРСЫ ВАЛЮТ И ЧЕСТНЫЙ ЖУРНАЛ.
//
// До этого среза `JournalEntry.Currency` не читал НИКТО: `GLPostingTx` клал
// `line.Debit` в книгу как есть. Валюта EUR, сумма 100 — в леджер уезжало 100
// гривен. Поле было объявлено и молча врало.
//
// Модель курса — коэффициент к БАЗОВОЙ валюте, а не попарные курсы: ровно то
// решение, которое воркспейс уже принял для единиц измерения 2026-08-31.
// Транзитивность бесплатна, противоречивую тройку нечем выразить.
public class CurrencyRateTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static IDocumentManager Docs => GetService<IDocumentManager>();
    private static ITotalsManager Totals => GetService<ITotalsManager>();
    private static ICurrencyRateService Rates => GetService<ICurrencyRateService>();

    private static readonly DateTime Day = new DateTime(2026, 6, 15);

    [IntegrationTest("Курс через базовую: транзитивность без попарного правила")]
    public async Task ConvertsThroughTheBaseCurrency()
    {
        var uah = await CurrencyAsync("BAS", 1m);        // базовая — та, у которой 1
        var eur = await CurrencyAsync("EUR", 45m);       // 1 EUR = 45 базовых
        var usd = await CurrencyAsync("USD", 41m);       // 1 USD = 41 базовая

        var toBase = await Rates.ConvertAsync(10m, eur, uah, Day);
        Assert.IsTrue(toBase == 450m, "10 EUR = 450 базовых, факт {0}", toBase?.ToString() ?? "null");

        // Правила «евро→доллар» НЕ заводили: оно считается через базовую.
        var cross = await Rates.ConvertAsync(41m, eur, usd, Day);
        Assert.IsTrue(cross == 45m, "41 EUR = 45 USD через базовую, факт {0}", cross?.ToString() ?? "null");

        // Тождество не требует строки курса: одновалютный тенант не обязан
        // заводить курс самому себе.
        var same = await Rates.ConvertAsync(7m, eur, eur, Day);
        Assert.IsTrue(same == 7m, "валюта сама в себя — это 7, факт {0}", same?.ToString() ?? "null");
    }

    [IntegrationTest("Нет окна на дату — null, а не единица")]
    public async Task MissingWindowIsNullNotOne()
    {
        var uah = await CurrencyAsync("BS2", 1m);
        var eur = await CurrencyAsync("EU2", 45m);

        // Курс заведён на июнь; спрашиваем про январь.
        var outside = await Rates.ConvertAsync(10m, eur, uah, new DateTime(2026, 1, 10));
        Assert.IsTrue(outside is null,
            "вне окна перевода нет; единица означала бы «валюты равны», факт {0}",
            outside?.ToString() ?? "null");
    }

    [IntegrationTest("Пересекающиеся окна одной валюты отклоняются")]
    public async Task OverlappingWindowsRejected()
    {
        var cur = await CurrencyAsync("OVL", 30m);       // окно 01.06…30.06

        var clash = Dict.NewRecord<ExchangeRate>();
        clash.Currency = cur;
        clash.RateToBase = 31m;
        clash.EffectiveFrom = new DateTime(2026, 6, 20);
        clash.EffectiveTo = new DateTime(2026, 7, 20);

        var failed = false; var message = "";
        try { await Dict.SaveRecordAsync(clash); }
        catch (Exception ex) { failed = true; message = ex.Message; }

        Assert.IsTrue(failed, "пересечение окон одной валюты обязано отклоняться");
        Assert.IsTrue(message.Contains("пересека"), "в отказе должна быть причина, факт: {0}", message);
    }

    [IntegrationTest("Проводка в чужой валюте уходит в книгу пересчитанной, а не как есть")]
    public async Task ForeignCurrencyEntryIsConverted()
    {
        var f = await LedgerFixtureAsync("FX1", functionalRate: 1m, documentRate: 45m);

        var je = await NewEntryAsync(f, currency: f.Foreign, amount: 100m);
        je.Subtype = JournalEntry.Subtypes.Posted;
        await Docs.SaveDocumentAsync(je);

        // 100 в валюте документа по курсу 45 = 4500 в валюте юрлица.
        // ДО этой правки в книгу уезжало 100 — ровно тот дефект.
        var debit = await GlAsync("Debit", f.DebitAccount);
        Assert.IsTrue(debit == 4500m,
            "в книге 100 × 45 = 4500, а не 100; факт {0}", debit);
    }

    [IntegrationTest("Валюта юрлица — курс 1, поведение прежнее")]
    public async Task FunctionalCurrencyIsUnchanged()
    {
        var f = await LedgerFixtureAsync("FX2", functionalRate: 1m, documentRate: 45m);

        var je = await NewEntryAsync(f, currency: f.Functional, amount: 100m);
        je.Subtype = JournalEntry.Subtypes.Posted;
        await Docs.SaveDocumentAsync(je);

        var debit = await GlAsync("Debit", f.DebitAccount);
        Assert.IsTrue(debit == 100m,
            "совпадающая валюта не пересчитывается: 100, факт {0}", debit);
    }

    [IntegrationTest("Чужая валюта без курса на дату проведения отклоняется")]
    public async Task ForeignCurrencyWithoutRateIsRejected()
    {
        var f = await LedgerFixtureAsync("FX3", functionalRate: 1m, documentRate: 45m);

        // Документ датирован ВНЕ окна курса.
        var je = await NewEntryAsync(f, currency: f.Foreign, amount: 100m,
                                     on: new DateTime(2026, 1, 10));

        var failed = false; var message = "";
        try
        {
            var reread = await Docs.GetDocumentAsync<JournalEntry>(je.MetaId);
            reread!.Subtype = JournalEntry.Subtypes.Posted;
            await Docs.SaveDocumentAsync(reread);
        }
        catch (Exception ex) { failed = true; message = ex.Message; }

        Assert.IsTrue(failed, "без курса проводка не должна проводиться");
        Assert.IsTrue(message.Contains("курса"), "в отказе должна быть причина, факт: {0}", message);
        Assert.IsTrue(message.Contains("2026-01-10"),
            "и ДАТА, потому что курс заводят именно на неё; факт: {0}", message);
    }

    // ── фикстура ────────────────────────────────────────────────────────────

    /// <summary>Валюта с окном курса 01.06.2026…30.06.2026.</summary>
    private async Task<Guid> CurrencyAsync(string code, decimal rateToBase)
    {
        var cur = Dict.NewRecord<Currency>();
        cur.Name = $"Cur-{code}";
        cur.Code = $"{code}{Db.NewId():N}"[..3].ToUpperInvariant();
        cur.Symbol = "¤";
        cur = await Dict.SaveRecordAsync(cur);

        var rate = Dict.NewRecord<ExchangeRate>();
        rate.Currency = cur.MetaId;
        rate.RateToBase = rateToBase;
        rate.EffectiveFrom = new DateTime(2026, 6, 1);
        rate.EffectiveTo = new DateTime(2026, 6, 30);
        await Dict.SaveRecordAsync(rate);

        return cur.MetaId;
    }

    private sealed class Fixture
    {
        public Guid LegalEntity;
        public Guid Functional;
        public Guid Foreign;
        public Guid DebitAccount;
        public Guid CreditAccount;
        public Guid FiscalPeriod;
    }

    private async Task<Fixture> LedgerFixtureAsync(string tag, decimal functionalRate, decimal documentRate)
    {
        var functional = await CurrencyAsync($"F{tag[^1]}", functionalRate);
        var foreign = await CurrencyAsync($"X{tag[^1]}", documentRate);

        var country = Dict.NewRecord<Country>();
        country.Name = $"Land-{tag}";
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "1";
        country = await Dict.SaveRecordAsync(country);

        var le = Dict.NewRecord<LegalEntity>();
        le.Name = $"Entity-{tag}";
        le.RegistrationNumber = $"R-{Db.NewId():N}"[..12];
        le.Country = country.MetaId;
        le.Currency = functional;
        le = await Dict.SaveRecordAsync(le);

        // Период обязателен у JournalEntry и должен накрывать ОБЕ даты теста:
        // июньскую (в окне курса) и январскую (вне его) — иначе отказ придёт
        // от закрытого периода, а не от отсутствующего курса, и тест доказал
        // бы не то.
        var fy = Dict.NewRecord<FiscalYear>();
        fy.Code = $"FY-{tag}";
        fy.StartDate = new DateTime(2026, 1, 1);
        fy.EndDate = new DateTime(2026, 12, 31);
        fy.IsClosed = false;
        fy = await Dict.SaveRecordAsync(fy);

        var period = Dict.NewRecord<FiscalPeriod>();
        period.FiscalYear = fy.MetaId;
        period.Code = $"P-{tag}";
        period.FromDate = new DateTime(2026, 1, 1);
        period.ToDate = new DateTime(2026, 12, 31);
        period.Status = "Open";
        period = await Dict.SaveRecordAsync(period);

        return new Fixture
        {
            FiscalPeriod = period.MetaId,
            LegalEntity = le.MetaId,
            Functional = functional,
            Foreign = foreign,
            DebitAccount = await AccountAsync($"D{tag}"),
            CreditAccount = await AccountAsync($"C{tag}"),
        };
    }

    private async Task<Guid> AccountAsync(string tag)
    {
        var acc = Dict.NewRecord<ChartOfAccounts>();
        acc.Code = $"{tag}{Db.NewId():N}"[..10];
        acc.Name = $"Account {tag}";
        return (await Dict.SaveRecordAsync(acc)).MetaId;
    }

    private async Task<JournalEntry> NewEntryAsync(
        Fixture f, Guid currency, decimal amount, DateTime? on = null)
    {
        var je = await Docs.NewDocumentAsync<JournalEntry>();
        je.LegalEntity = f.LegalEntity;
        je.FiscalPeriod = f.FiscalPeriod;
        je.Currency = currency;
        je.DocumentDate = on ?? Day;
        je.Description = $"FX {Db.NewId():N}"[..20];
        je.Lines.Add(new JournalEntryLinesTablePartRow { Account = f.DebitAccount, Debit = amount });
        je.Lines.Add(new JournalEntryLinesTablePartRow { Account = f.CreditAccount, Credit = amount });
        await Docs.SaveDocumentAsync(je);
        return (await Docs.GetDocumentAsync<JournalEntry>(je.MetaId))!;
    }

    /// <summary>GL разрезан ДИНАМИЧЕСКИМИ аналитиками — точечного среза нет,
    /// поэтому суммируется движение по счёту этого теста.</summary>
    private static async Task<decimal> GlAsync(string resource, Guid account)
        => await Totals.GetBalanceAsync("GL", resource,
               new Dictionary<string, object?> { ["Account"] = account });
}
