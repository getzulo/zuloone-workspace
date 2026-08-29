using System;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Покрытие UnitConversionService: перевод по прямому правилу, по обратному
// (автоматически 1/Factor) и тождество (одинаковые единицы).
//
// Данные готовятся ТИПИЗИРОВАННЫМ IDictionaryManager<T>: тест идёт тем же путём,
// что и бизнес-код, а опечатка в имени поля ловится компилятором.
public class UnitConversionServiceTest : IntegrationTestScriptBase
{
    private static Task<Guid> NewUnitAsync(string name, string code, int? decimals = null)
        => NewRecordAsync<UnitOfMeasure>(u =>
        {
            u.Name = name;
            u.Code = code;
            if (decimals.HasValue) u.DecimalPlaces = decimals.Value;
        });

    [IntegrationTest("Конвертация единиц: прямое, обратное правило и тождество")]
    public async Task Converts()
    {
        var box = await NewUnitAsync("Box", "BOX");
        var piece = await NewUnitAsync("Piece", "PCS");
        await NewRecordAsync<UnitConversion>(c => { c.FromUnit = box; c.ToUnit = piece; c.Factor = 12m; });

        var svc = GetService<IUnitConversionService>();

        Assert.IsTrue(await svc.ConvertAsync(2m, box, piece) == 24m, "2 короба = 24 шт (прямое ×12)");
        Assert.IsTrue(await svc.ConvertAsync(24m, piece, box) == 2m, "24 шт = 2 короба (обратное 1/12)");
        Assert.IsTrue(await svc.ConvertAsync(5m, box, box) == 5m, "тождество: единица в себя");
        Assert.IsTrue(await svc.FactorAsync(box, piece) == 12m, "коэффициент box→piece = 12");
    }

    [IntegrationTest("Точность по единице: 2 г колбасы × 10 бутербродов = 0.020 кг; шт=0; м=2")]
    public async Task RoundsToUnitPrecision()
    {
        // кг держит 3 знака (граммы), граммы 0, штуки 0, метры 2.
        var kg = await NewUnitAsync("Kilogram", "KG", 3);
        var g = await NewUnitAsync("Gram", "G", 0);
        var pcs = await NewUnitAsync("Piece", "PC", 0);
        var m = await NewUnitAsync("Meter", "M", 2);
        await NewRecordAsync<UnitConversion>(c => { c.FromUnit = g; c.ToUnit = kg; c.Factor = 0.001m; }); // 1 г = 0.001 кг

        var svc = GetService<IUnitConversionService>();

        // Колбаса закупается в кг, спецификация — в граммах: 2 г × 10 бутербродов = 20 г → 0.020 кг.
        Assert.IsTrue(await svc.ConvertRoundedAsync(20m, g, kg) == 0.020m,
            "20 г → 0.020 кг (3 знака), факт {0}", await svc.ConvertRoundedAsync(20m, g, kg));
        // 2 кг колбасы обратно в граммы (точно).
        Assert.IsTrue(await svc.ConvertRoundedAsync(2m, kg, g) == 2000m,
            "2 кг → 2000 г, факт {0}", await svc.ConvertRoundedAsync(2m, kg, g));
        // Булки в штуках — 0 знаков.
        Assert.IsTrue(await svc.ConvertRoundedAsync(10m, pcs, pcs) == 10m, "10 булок = 10 шт");
        // Плёнка в метрах — 2 знака.
        Assert.IsTrue(await svc.ConvertRoundedAsync(1.5m, m, m) == 1.50m, "1.5 м (2 знака)");
        // Точность единицы читается корректно.
        Assert.IsTrue(await svc.PrecisionAsync(kg) == 3, "точность кг = 3");
        Assert.IsTrue(await svc.PrecisionAsync(pcs) == 0, "точность шт = 0");
    }
}
