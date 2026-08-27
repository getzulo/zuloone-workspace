using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Покрытие UnitConversionService: перевод по прямому правилу, по обратному
// (автоматически 1/Factor) и тождество (одинаковые единицы).
public class UnitConversionServiceTest : IntegrationTestScriptBase
{
    [IntegrationTest("Конвертация единиц: прямое, обратное правило и тождество")]
    public async Task Converts()
    {
        var box = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Box", ["Code"] = "BOX" });
        var piece = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS" });
        await Db.InsertAsync("UnitConversion", new Dictionary<string, object?>
            { ["FromUnit"] = box, ["ToUnit"] = piece, ["Factor"] = 12m });

        var svc = GetService<IUnitConversionService>();

        Assert.IsTrue(await svc.ConvertAsync(2m, (Guid)box, (Guid)piece) == 24m, "2 короба = 24 шт (прямое ×12)");
        Assert.IsTrue(await svc.ConvertAsync(24m, (Guid)piece, (Guid)box) == 2m, "24 шт = 2 короба (обратное 1/12)");
        Assert.IsTrue(await svc.ConvertAsync(5m, (Guid)box, (Guid)box) == 5m, "тождество: единица в себя");
        Assert.IsTrue(await svc.FactorAsync((Guid)box, (Guid)piece) == 12m, "коэффициент box→piece = 12");
    }

    [IntegrationTest("Точность по единице: 2 г колбасы × 10 бутербродов = 0.020 кг; шт=0; м=2")]
    public async Task RoundsToUnitPrecision()
    {
        // кг держит 3 знака (граммы), граммы 0, штуки 0, метры 2.
        var kg = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Kilogram", ["Code"] = "KG", ["DecimalPlaces"] = 3 });
        var g = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Gram", ["Code"] = "G", ["DecimalPlaces"] = 0 });
        var pcs = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PC", ["DecimalPlaces"] = 0 });
        var m = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Meter", ["Code"] = "M", ["DecimalPlaces"] = 2 });
        await Db.InsertAsync("UnitConversion", new Dictionary<string, object?>
            { ["FromUnit"] = g, ["ToUnit"] = kg, ["Factor"] = 0.001m }); // 1 г = 0.001 кг

        var svc = GetService<IUnitConversionService>();

        // Колбаса закупается в кг, спецификация — в граммах: 2 г × 10 бутербродов = 20 г → 0.020 кг.
        Assert.IsTrue(await svc.ConvertRoundedAsync(20m, (Guid)g, (Guid)kg) == 0.020m, "20 г → 0.020 кг (3 знака), факт {0}", await svc.ConvertRoundedAsync(20m, (Guid)g, (Guid)kg));
        // 2 кг колбасы обратно в граммы (точно).
        Assert.IsTrue(await svc.ConvertRoundedAsync(2m, (Guid)kg, (Guid)g) == 2000m, "2 кг → 2000 г, факт {0}", await svc.ConvertRoundedAsync(2m, (Guid)kg, (Guid)g));
        // Булки в штуках — 0 знаков.
        Assert.IsTrue(await svc.ConvertRoundedAsync(10m, (Guid)pcs, (Guid)pcs) == 10m, "10 булок = 10 шт");
        // Плёнка в метрах — 2 знака.
        Assert.IsTrue(await svc.ConvertRoundedAsync(1.5m, (Guid)m, (Guid)m) == 1.50m, "1.5 м (2 знака)");
        // Точность единицы читается корректно.
        Assert.IsTrue(await svc.PrecisionAsync((Guid)kg) == 3, "точность кг = 3");
        Assert.IsTrue(await svc.PrecisionAsync((Guid)pcs) == 0, "точность шт = 0");
    }
}
