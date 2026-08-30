using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;
// Сгенерированные классы сущностей (TaxCalculation, TaxCode, TaxCalculationLinesTablePartRow…).
// Тестовые скрипты НЕ получают это пространство имён глобальным using: без него
// генерённые классы не находятся, а `Currency` вдобавок цепляется за посторонний
// недоступный тип, и ошибка компилятора описывает не ту причину.
using ZuloOne.Runtime.Generated;

// Integration coverage for the Tax core: finalizing a tax calculation posts base and
// amount into the tax ledger, and the finalization guard refuses a line whose rate
// was not the one in force on the calculation's tax point.
//
// ПРО ЧТО ЗДЕСЬ ПАРЫ КЕЙСОВ. Проверку «ставка строки та, что действовала на
// TaxPointDate» нельзя доказать одним кейсом: отказ на неверной ставке приходит
// ОДИНАКОВО и от правильной проверки, и от проверки, которая просто берёт
// сегодняшнюю ставку. Различает их только пара — расчёт 2024 года по ставке
// 2024 года ПРОХОДИТ, а тот же расчёт по ставке 2025 года НЕ проходит, — плюс
// третий кейс на «ставки на дату нет вовсе».
//
// Написано так, как пишется прикладной сервис: типизированные сущности через
// менеджеры. Запись — NewRecord<T> → заполнить → SaveRecordAsync; документ —
// NewDocumentAsync<T> → заполнить Lines → SaveDocumentAsync; финализация — это
// ПРИСВОЕНИЕ подтипа плюс сохранение (MIQS doc.SubtypeID = …; SaveDocument).
public class TaxCalculationPostingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();
    // Не «Tax»: так зовётся генерённый справочник, и имя перекрыло бы тип.
    private static ITaxService Svc => GetService<ITaxService>();

    private sealed class Setup
    {
        public Guid LegalEntity;
        public Guid Currency;
        public Guid TaxId;
        public Guid TaxCode;
        public Guid Direction;
    }

    /// <summary>
    /// Налоговый контур: ставка 0.15 с 01.01.2020. <paramref name="rateTo"/>
    /// закрывает её окно — тогда следующую ставку кейс заводит сам через
    /// <see cref="AddRateAsync"/>. Окна налога и кода всегда открыты: предметом
    /// проверки здесь является ставка.
    /// </summary>
    private async Task<Setup> SetupAsync(DateTime? rateTo = null)
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Saudi Riyal";
        currency.Code = "SAR";
        currency.Symbol = "﷼";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Saudi Arabia";
        country.CodeISO2 = "SA";
        country.CodeISO3 = "SAU";
        country.PhoneCode = "966";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME KSA";
        legalEntity.RegistrationNumber = "REG-TAX-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = "ZATCA";
        authority.Name = "ZATCA";
        authority.CountryCode = "SA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var taxType = DictionaryManager.NewRecord<TaxType>();
        taxType.Code = "VAT";
        taxType.Name = "Value added tax";
        taxType.Category = "VAT";
        taxType = await DictionaryManager.SaveRecordAsync(taxType);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = "SA";
        jurisdiction.Name = "Saudi Arabia";
        jurisdiction.CountryCode = "SA";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var from = new DateTime(2020, 1, 1);
        // EffectiveTo налога и кода НЕ заполняется: окно открыто справа. Поле
        // необязательное, поэтому генерируется как DateTime? и уходит в базу NULL.

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = "SA-VAT";
        tax.Name = "Saudi VAT";
        tax.TaxType = taxType.MetaId;
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = from;
        tax = await DictionaryManager.SaveRecordAsync(tax);

        var rate = DictionaryManager.NewRecord<TaxRate>();
        rate.Tax = tax.MetaId;
        rate.Code = "SA-VAT-15";
        rate.Rate = 0.15m;
        rate.EffectiveFrom = from;
        rate.EffectiveTo = rateTo;
        rate = await DictionaryManager.SaveRecordAsync(rate);

        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = "STD";
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = "SA-VAT-15";
        code.Name = "Standard 15%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        code = await DictionaryManager.SaveRecordAsync(code);

        var direction = DictionaryManager.NewRecord<TaxDirection>();
        direction.Code = "OUTPUT";
        direction.Name = "Output";
        direction = await DictionaryManager.SaveRecordAsync(direction);

        return new Setup
        {
            LegalEntity = legalEntity.MetaId,
            Currency = currency.MetaId,
            TaxId = tax.MetaId,
            TaxCode = code.MetaId,
            Direction = direction.MetaId,
        };
    }

    /// <summary>Следующая ставка ТОГО ЖЕ налога — история ставок и есть строки
    /// TaxRate с общим Tax и непересекающимися окнами.</summary>
    private async Task AddRateAsync(Setup s, string code, decimal rate, DateTime from)
    {
        var next = DictionaryManager.NewRecord<TaxRate>();
        next.Tax = s.TaxId;
        next.Code = code;
        next.Rate = rate;
        next.EffectiveFrom = from;
        await DictionaryManager.SaveRecordAsync(next);
    }

    // Подтип не передаём намеренно: NewDocumentAsync обязан подставить НАЧАЛЬНЫЙ
    // подтип типа (Draft), а дальше сработает объявленный маршрут Draft → Finalized.
    private async Task<TaxCalculation> NewCalcAsync(
        Setup s, decimal taxBase, decimal amount, decimal rate = 0.15m, DateTime? taxPoint = null)
    {
        var calc = await DocumentManager.NewDocumentAsync<TaxCalculation>();
        calc.LegalEntity = s.LegalEntity;
        calc.Currency = s.Currency;
        calc.TaxPointDate = taxPoint ?? new DateTime(2026, 8, 20);
        calc.Lines.Add(new TaxCalculationLinesTablePartRow
        {
            TaxCode = s.TaxCode,
            TaxBase = taxBase,
            RateValue = rate,
            TaxAmount = amount,
            Direction = s.Direction,
        });
        await DocumentManager.SaveDocumentAsync(calc);
        return calc;
    }

    /// <summary>
    /// Переводит расчёт в Finalized и возвращает ПРИЧИНУ отказа; пустая строка
    /// означает, что финализация прошла (у исключения всегда есть сообщение).
    ///
    /// Отказ приходит исключением, а бросок происходит внутри окружающей
    /// транзакции прогона и обрекает её: после неудачной финализации к базе
    /// обращаться нельзя. Поэтому кейс сначала утверждает про саму причину, и
    /// только успешный переход позволяет читать движения дальше.
    /// </summary>
    private static async Task<string> TryFinalizeAsync(TaxCalculation calc)
    {
        try
        {
            calc.Subtype = TaxCalculation.Subtypes.Finalized;
            await DocumentManager.SaveDocumentAsync(calc);
            return "";
        }
        catch (Exception ex)
        {
            var reason = "";
            for (Exception? e = ex; e is not null; e = e.InnerException) reason += e.Message + " | ";
            return reason.Length == 0 ? "(отказ без сообщения)" : reason;
        }
    }

    [IntegrationTest("Финализация налогового расчёта разносит базу и сумму в TaxLedger")]
    public async Task FinalizePostsToLedger()
    {
        var s = await SetupAsync();
        var calc = await NewCalcAsync(s, taxBase: 100m, amount: 15m);

        // Черновик леджер не двигает. Проверяем ДО перехода: тип помечен postOnSave,
        // и без этой проверки утверждения ниже проходят даже тогда, когда расчёт
        // разнёсся сам на сохранении — то есть про переход тест не доказывает ничего.
        Assert.IsTrue((await TotalsManager.QueryMovementsAsync("TaxLedger")).Count == 0,
            "черновик не должен порождать движений TaxLedger");

        var reason = await TryFinalizeAsync(calc);
        Assert.IsTrue(reason.Length == 0, "правильный расчёт обязан финализироваться, отказ: {0}", reason);

        var movements = await TotalsManager.QueryMovementsAsync("TaxLedger");
        Assert.IsTrue(movements.Count == 1, "ожидалось 1 движение TaxLedger, а не {0}", movements.Count);
        Assert.IsTrue(Convert.ToDecimal(movements[0]["TaxBase"]) == 100m, "база должна быть 100, а не {0}", movements[0]["TaxBase"]);
        Assert.IsTrue(Convert.ToDecimal(movements[0]["TaxAmount"]) == 15m, "сумма налога должна быть 15, а не {0}", movements[0]["TaxAmount"]);
    }

    [IntegrationTest("Расчёт по ставке СВОЕЙ даты финализируется, хотя сегодня действует другая")]
    public async Task RateOfTheTaxPointDateFinalizes()
    {
        var s = await SetupAsync(rateTo: new DateTime(2024, 12, 31));
        await AddRateAsync(s, "SA-VAT-20", 0.20m, new DateTime(2025, 1, 1));

        // Сегодня у кода действует 0.20 — значит успех ниже доказывает, что
        // финализация спрашивает ставку НА ДАТУ РАСЧЁТА. Проверка, подставляющая
        // «сегодня», отвергла бы совершенно правильный документ 2024 года, и
        // задним числом выпустить расчёт стало бы невозможно.
        var today = await Svc.ResolveRateAsync(s.TaxCode);
        Assert.IsTrue(today == 0.20m, "сегодня у кода действует 0.20, факт {0}", today.HasValue ? today.Value : -1m);

        var calc = await NewCalcAsync(s, taxBase: 100m, amount: 15m, rate: 0.15m, taxPoint: new DateTime(2024, 6, 1));
        Assert.IsTrue((await TotalsManager.QueryMovementsAsync("TaxLedger")).Count == 0,
            "черновик не должен порождать движений TaxLedger");

        var reason = await TryFinalizeAsync(calc);
        Assert.IsTrue(reason.Length == 0, "расчёт по ставке своей даты обязан финализироваться, отказ: {0}", reason);

        var movements = await TotalsManager.QueryMovementsAsync("TaxLedger");
        Assert.IsTrue(movements.Count == 1, "ожидалось 1 движение TaxLedger, а не {0}", movements.Count);
        Assert.IsTrue(Convert.ToDecimal(movements[0]["TaxAmount"]) == 15m,
            "разнестись должна сумма по ставке 2024 года — 15, а не {0}", movements[0]["TaxAmount"]);
    }

    [IntegrationTest("Ставка, не действовавшая на дату налогового события, не финализируется")]
    public async Task RateNotEffectiveOnTaxPointIsRejected()
    {
        var s = await SetupAsync(rateTo: new DateTime(2024, 12, 31));
        await AddRateAsync(s, "SA-VAT-20", 0.20m, new DateTime(2025, 1, 1));

        // Ставка 0.20 у этого налога ЕСТЬ — но её окно начинается в 2025-м, а
        // расчёт датирован 2024-м. Проверка «такая ставка у налога вообще
        // заведена» это пропустила бы; ловит только сравнение с окном даты.
        var calc = await NewCalcAsync(s, taxBase: 100m, amount: 20m, rate: 0.20m, taxPoint: new DateTime(2024, 6, 1));

        // Строка САМОСОГЛАСОВАНА: 100 × 0.20 = 20. Ровно поэтому одной арифметики
        // не хватало — она сходится, а число неверное, и неверным уходит в
        // декларацию. Считаем ожидаемое сервисом, а не повторяя умножение здесь:
        // тавтологичное утверждение не доказало бы самосогласованности.
        Assert.IsTrue(Svc.CalculateTax(100m, 0.20m) == 20m,
            "строка обязана быть самосогласованной, иначе отказ ниже доказывает не то");
        Assert.IsTrue((await TotalsManager.QueryMovementsAsync("TaxLedger")).Count == 0,
            "черновик не должен порождать движений TaxLedger");

        var reason = await TryFinalizeAsync(calc);
        Assert.IsTrue(reason.Length > 0, "расчёт по ставке чужого периода должен быть отклонён при финализации");
        Assert.IsTrue(reason.Contains("не действовала на"),
            "отказ должен быть именно про ставку, не действовавшую на дату расчёта, факт: {0}", reason);
        Assert.IsFalse(reason.Contains("не сходится"),
            "отказ пришёл от арифметики, а не от проверки ставки, факт: {0}", reason);
    }

    [IntegrationTest("Отозванная ставка: на дату расчёта ставки нет — финализация отклоняется")]
    public async Task RetiredRateBlocksFinalization()
    {
        // Единственная ставка закрыта в 2020-м, а расчёт датирован 2026-м: строка
        // ссылается на ставку, которой на её дату уже не существует. Пропустить
        // такой расчёт значило бы выпустить сумму, обосновать которую нечем —
        // «ставки нет» это не «ставка 0%».
        var s = await SetupAsync(rateTo: new DateTime(2020, 12, 31));
        var taxPoint = new DateTime(2026, 8, 20);

        var calc = await NewCalcAsync(s, taxBase: 100m, amount: 15m, rate: 0.15m, taxPoint: taxPoint);

        Assert.IsNull(await Svc.ResolveRateAsync(s.TaxCode, taxPoint),
            "на дату расчёта действующей ставки быть не должно — иначе кейс проверяет не то");
        Assert.IsTrue((await TotalsManager.QueryMovementsAsync("TaxLedger")).Count == 0,
            "черновик не должен порождать движений TaxLedger");

        var reason = await TryFinalizeAsync(calc);
        Assert.IsTrue(reason.Length > 0, "расчёт без действующей на его дату ставки должен быть отклонён");
        Assert.IsTrue(reason.Contains("нет действующей ставки"),
            "отказ должен называть именно отсутствие действующей ставки, факт: {0}", reason);
    }

    [IntegrationTest("Расхождение база×ставка≠сумма отклоняется")]
    public async Task AmountMismatchIsRejected()
    {
        var s = await SetupAsync();

        // Черновик с неверной суммой обязан СОХРАНИТЬСЯ: черновику позволено быть
        // неправильным, проверка принадлежит ФИНАЛИЗАЦИИ. И в леджер он не попадает.
        var calc = await NewCalcAsync(s, taxBase: 100m, amount: 20m); // 100 × 0.15 = 15, не 20
        Assert.IsTrue((await TotalsManager.QueryMovementsAsync("TaxLedger")).Count == 0,
            "неверный черновик не должен порождать движений TaxLedger");

        var reason = await TryFinalizeAsync(calc);
        Assert.IsTrue(reason.Length > 0, "расчёт с неверной суммой налога должен быть отклонён при финализации");
        // Ставка здесь ПРАВИЛЬНАЯ, поэтому отказ обязан прийти именно от
        // арифметики: иначе проверка ставки перехватила бы кейс на себя, и
        // покрытие суммы тихо исчезло бы.
        Assert.IsTrue(reason.Contains("не сходится"),
            "отказ должен быть именно про несходящуюся сумму, факт: {0}", reason);
    }
}
