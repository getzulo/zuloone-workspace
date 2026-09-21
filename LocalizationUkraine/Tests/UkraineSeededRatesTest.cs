using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Ставки по ПОСТАВЛЯЕМЫМ сидам, а не по контуру, собранному самим тестом.
//
// Почему этот файл вообще существует. UkraineVatFlowTest и саудовский
// VatCircuitAsync создают СВОЙ налог под каждую ставку и каждую трактовку —
// то есть обходят дефект вместо того, чтобы его показать. Из-за этого
// vat-UA.json месяцами лежал в поставке нерабочим: 20% и 7% висели на одном
// налоге без окончания действия, и ResolveRateAsync на любой сегодняшней дате
// падал на «действует больше одной ставки». Тест, который строит данные сам,
// такое поймать не может по устройству.
//
// Здесь нет ни одного созданного справочника. Только то, что установщик уже
// положил на стенд.
public class UkraineSeededRatesTest : IntegrationTestScriptBase
{
    private static IDictionaryManager<TaxCode> Codes => GetService<IDictionaryManager<TaxCode>>();
    private static IDictionaryManager<Tax> Taxes => GetService<IDictionaryManager<Tax>>();
    private static IDictionaryManager<TaxRate> Rates => GetService<IDictionaryManager<TaxRate>>();
    private static ITaxService Svc => GetService<ITaxService>();

    private static async Task<TaxCode> CodeAsync(string code)
    {
        var rows = await Codes.GetRecordsAsync($"Code = '{code}'", take: 2);
        Assert.IsTrue(rows.Count == 1,
            "налоговый код «{0}» должен существовать в поставке ровно один раз, найдено {1}",
            code, rows.Count);
        return rows[0];
    }

    private static async Task<decimal?> RateAsync(string code, DateTime? on = null)
        => await Svc.ResolveRateAsync((await CodeAsync(code)).MetaId, on);

    [IntegrationTest("Сид Украины: 20% и 7% действуют одновременно и не спорят")]
    public async Task StandardAndReducedCoexist()
    {
        var std = await RateAsync("UA-S");
        Assert.IsTrue(std == 0.20m, "UA-S по сиду = 0.20, факт {0}", std?.ToString() ?? "null");

        var reduced = await RateAsync("UA-7");
        Assert.IsTrue(reduced == 0.07m, "UA-7 по сиду = 0.07, факт {0}", reduced?.ToString() ?? "null");
    }

    [IntegrationTest("Сид Украины: нулевая и освобождённая дают 0, а не ставку налога")]
    public async Task ZeroTreatmentsResolveToZero()
    {
        var zero = await RateAsync("UA-Z");
        Assert.IsTrue(zero == 0m, "UA-Z обязан дать 0, факт {0}", zero?.ToString() ?? "null");

        var exempt = await RateAsync("UA-E");
        Assert.IsTrue(exempt == 0m, "UA-E обязан дать 0, факт {0}", exempt?.ToString() ?? "null");
    }

    [IntegrationTest("Сид Украины: счёт задним числом берёт ставку своей даты")]
    public async Task BackdatedUsesHistoricalRate()
    {
        var on2016 = await RateAsync("UA-S", new DateTime(2016, 6, 1));
        Assert.IsTrue(on2016 == 0.20m, "UA-S на 2016-06-01 = 0.20, факт {0}", on2016?.ToString() ?? "null");

        // 7% введены с 2014-04-01: до этой даты ставки в пониженной полосе нет,
        // и это null («ставки нет»), а не 0 («ставка ноль») — разные вещи.
        var before = await RateAsync("UA-7", new DateTime(2014, 1, 15));
        Assert.IsNull(before, "до 2014-04-01 у UA-7 ставки нет, факт {0}", before?.ToString() ?? "null");
    }

    [IntegrationTest("Страны не делят один налог: у украинских кодов свой Tax")]
    public async Task UkraineOwnsItsTax()
    {
        var ua = await CodeAsync("UA-S");
        var tax = await Taxes.GetRecordAsync(ua.Tax);
        Assert.IsNotNull(tax, "у UA-S должен быть налог");
        Assert.IsTrue(tax!.Code == "VAT-UA",
            "украинские коды обязаны висеть на собственном налоге VAT-UA, факт «{0}». "
            + "Общий налог означает, что ставки двух стран складываются в одну корзину "
            + "и разрешение ставки падает на неоднозначности", tax.Code);

        // И ни одна ставка этого налога не принадлежит другой стране.
        var rates = await Rates.GetRecordsAsync($"Tax = '{tax.MetaId}'");
        var codes = rates.Select(r => r.Code).OrderBy(c => c).ToList();
        Assert.IsTrue(codes.SequenceEqual(new[] { "VAT20", "VAT7" }),
            "у VAT-UA ровно ставки VAT20 и VAT7, факт [{0}]", string.Join(", ", codes));
    }

    [IntegrationTest("Ставки одной категории не пересекаются по датам")]
    public async Task NoOverlapWithinCategory()
    {
        var ua = await CodeAsync("UA-S");
        var rates = await Rates.GetRecordsAsync($"Tax = '{ua.Tax}'");

        var byCategory = rates.GroupBy(r => r.TaxCategory);
        foreach (var band in byCategory)
        {
            var list = band.ToList();
            for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
            {
                var a = list[i];
                var b = list[j];
                var overlap = a.EffectiveFrom.Date <= (b.EffectiveTo?.Date ?? DateTime.MaxValue)
                           && b.EffectiveFrom.Date <= (a.EffectiveTo?.Date ?? DateTime.MaxValue);
                Assert.IsTrue(!overlap,
                    "ставки «{0}» и «{1}» в одной категории пересекаются по датам — "
                    + "именно на этом ResolveRateAsync отказывается считать", a.Code, b.Code);
            }
        }
    }

    // ═══ ЄДИНИЙ ПОДАТОК ══════════════════════════════════════════════════════
    // Ровно те же грабли, что утопили ПДВ: две ставки одного налога БЕЗ
    // категорий — и ResolveRateAsync падает на «действует больше одной ставки»
    // вместо того, чтобы вернуть 3% или 5%. Драйвер такой отказ проглатывает
    // молча (нет ставки — нет начисления), так что поймать это может только
    // тест по ПОСТАВЛЯЕМЫМ данным.

    [IntegrationTest("Сид Украины: ставки единого налога 3% и 5% разрешаются, а не спорят")]
    public async Task SingleTaxRatesResolve()
    {
        var three = await RateAsync("UA-EP3");
        Assert.IsTrue(three == 0.03m, "UA-EP3 по сиду = 0.03, факт {0}", three?.ToString() ?? "null");

        var five = await RateAsync("UA-EP5");
        Assert.IsTrue(five == 0.05m, "UA-EP5 по сиду = 0.05, факт {0}", five?.ToString() ?? "null");
    }

    [IntegrationTest("Сид Украины: единый налог — отдельный налог, не полоса внутри ПДВ")]
    public async Task SingleTaxOwnsItsTax()
    {
        var ep = await CodeAsync("UA-EP3");
        var tax = await Taxes.GetRecordAsync(ep.Tax);
        Assert.IsNotNull(tax, "у UA-EP3 должен быть налог");
        Assert.IsTrue(tax!.Code == "EP-UA",
            "единый налог обязан висеть на собственном EP-UA, факт «{0}». Посади его на "
            + "VAT-UA — и 3%, 5%, 20%, 7% окажутся ставками одного налога, а разрешение "
            + "ставки начнёт падать на неоднозначности", tax.Code);

        var vat = await CodeAsync("UA-S");
        Assert.IsTrue(vat.Tax != ep.Tax,
            "ПДВ и ЄП — разные налоги, а оказались одним");

        // Каждая ставка ЄП живёт в СВОЕЙ категории: без этого все попадают в
        // безкатегорийную полосу налога и спорят между собой. Ставок ТРИ:
        // обычные 3% и 5% плюс 15% на превышение годового предела (ПКУ 293.4).
        // Превышение — тот же налог по штрафной ставке, отдельного налога под
        // него не заводится.
        var rates = await Rates.GetRecordsAsync($"Tax = '{tax.MetaId}'");
        var codes = rates.Select(r => r.Code).OrderBy(c => c).ToList();
        Assert.IsTrue(codes.SequenceEqual(new[] { "EP15", "EP3", "EP5" }),
            "у EP-UA ровно ставки EP3, EP5 и EP15, факт [{0}]", string.Join(", ", codes));

        var uncategorised = rates.Where(r => r.TaxCategory == Guid.Empty).Select(r => r.Code).ToList();
        Assert.IsTrue(uncategorised.Count == 0,
            "ставки ЄП обязаны иметь категорию, иначе они спорят друг с другом: без категории [{0}]",
            string.Join(", ", uncategorised));
    }

    [IntegrationTest("Сид Украины: зарплатные налоги отдельные, а ставка ВЗ сменилась 01.12.2024")]
    public async Task PayrollTaxesAreSeparateAndDated()
    {
        var pdfo = await RateAsync("UA-PDFO");
        Assert.IsTrue(pdfo == 0.18m, "ПДФО по сиду = 0.18, факт {0}", pdfo?.ToString() ?? "null");

        // Военный сбор жил по 1,5% десять лет и с 01.12.2024 стал 5%. Окно
        // первой ставки обязано быть ЗАКРЫТО: две открытые вправо ставки одной
        // категории — ровно то, на чём ResolveRateAsync отказывается считать,
        // и проявилось бы это не здесь, а при начислении зарплаты.
        var before = await RateAsync("UA-VZ", new DateTime(2024, 11, 30));
        Assert.IsTrue(before == 0.015m, "на 30.11.2024 ВЗ = 0.015, факт {0}", before?.ToString() ?? "null");

        var after = await RateAsync("UA-VZ", new DateTime(2024, 12, 1));
        Assert.IsTrue(after == 0.05m, "на 01.12.2024 ВЗ = 0.05, факт {0}", after?.ToString() ?? "null");

        // ПДФО и ВЗ — РАЗНЫЕ налоги: они идут в разные бюджеты и в разные
        // строки расчёта, и свернуть их в один налог с двумя категориями
        // значило бы потерять это различие навсегда.
        var pdfoCode = await CodeAsync("UA-PDFO");
        var vzCode = await CodeAsync("UA-VZ");
        Assert.IsTrue(pdfoCode.Tax != vzCode.Tax,
            "ПДФО и військовий збір обязаны висеть на разных налогах");
    }
}
