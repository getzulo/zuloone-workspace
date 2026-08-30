using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей (TaxCalculation, TaxCode, TaxCalculationLinesTablePartRow…).
// Тестовые скрипты НЕ получают это пространство имён глобальным using: без него
// генерённые классы не находятся, а `Currency` вдобавок цепляется за посторонний
// недоступный тип, и ошибка компилятора описывает не ту причину.
using ZuloOne.Runtime.Generated;

// Integration coverage for the Tax core: finalizing a tax calculation posts base and
// amount into the tax ledger, and the base×rate=amount guard rejects a mismatch.
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

    private sealed class Setup
    {
        public Guid LegalEntity;
        public Guid Currency;
        public Guid TaxCode;
        public Guid Direction;
    }

    private async Task<Setup> SetupAsync()
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
        // EffectiveTo НЕ заполняется: окно действия открыто справа. Поле
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
            TaxCode = code.MetaId,
            Direction = direction.MetaId,
        };
    }

    // Подтип не передаём намеренно: NewDocumentAsync обязан подставить НАЧАЛЬНЫЙ
    // подтип типа (Draft), а дальше сработает объявленный маршрут Draft → Finalized.
    private async Task<TaxCalculation> NewCalcAsync(Setup s, decimal taxBase, decimal amount)
    {
        var calc = await DocumentManager.NewDocumentAsync<TaxCalculation>();
        calc.LegalEntity = s.LegalEntity;
        calc.Currency = s.Currency;
        calc.TaxPointDate = new DateTime(2026, 8, 20);
        calc.Lines.Add(new TaxCalculationLinesTablePartRow
        {
            TaxCode = s.TaxCode,
            TaxBase = taxBase,
            RateValue = 0.15m,
            TaxAmount = amount,
            Direction = s.Direction,
        });
        await DocumentManager.SaveDocumentAsync(calc);
        return calc;
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

        calc.Subtype = TaxCalculation.Subtypes.Finalized;
        await DocumentManager.SaveDocumentAsync(calc);

        var movements = await TotalsManager.QueryMovementsAsync("TaxLedger");
        Assert.IsTrue(movements.Count == 1, "ожидалось 1 движение TaxLedger, а не {0}", movements.Count);
        Assert.IsTrue(Convert.ToDecimal(movements[0]["TaxBase"]) == 100m, "база должна быть 100, а не {0}", movements[0]["TaxBase"]);
        Assert.IsTrue(Convert.ToDecimal(movements[0]["TaxAmount"]) == 15m, "сумма налога должна быть 15, а не {0}", movements[0]["TaxAmount"]);
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

        // Отказ приходит ИСКЛЮЧЕНИЕМ, а бросок происходит внутри окружающей
        // транзакции прогона и обрекает её. Поэтому после catch к базе больше не
        // обращаемся — утверждение делается о самом отказе.
        var rejected = false;
        try
        {
            calc.Subtype = TaxCalculation.Subtypes.Finalized;
            await DocumentManager.SaveDocumentAsync(calc);
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "расчёт с неверной суммой налога должен быть отклонён при финализации");
    }
}
