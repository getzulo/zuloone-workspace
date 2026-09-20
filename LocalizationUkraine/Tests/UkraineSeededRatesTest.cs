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
}
