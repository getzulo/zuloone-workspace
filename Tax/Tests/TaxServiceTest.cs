using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Покрытие TaxService: синхронный расчёт налога (база × ставка, округлённый) и
// разрешение действующей ставки по налоговому коду (TaxCode → TaxRate).
public class TaxServiceTest : IntegrationTestScriptBase
{
    [IntegrationTest("Сумма налога = база × ставка, округлённая до денежной точности")]
    public async Task CalculatesTax()
    {
        await Task.CompletedTask;
        var tax = GetService<ITaxService>();

        Assert.IsTrue(tax.CalculateTax(100m, 0.15m) == 15m, "100 × 0.15 = 15, факт {0}", tax.CalculateTax(100m, 0.15m));
        Assert.IsTrue(tax.CalculateTax(33.33m, 0.2m) == 6.67m, "33.33 × 0.2 = 6.666 → 6.67, факт {0}", tax.CalculateTax(33.33m, 0.2m));
        Assert.IsTrue(tax.CalculateTax(0m, 0.15m) == 0m, "0 × 0.15 = 0, факт {0}", tax.CalculateTax(0m, 0.15m));
    }

    [IntegrationTest("Ставка разрешается по налоговому коду (TaxCode → TaxRate)")]
    public async Task ResolvesRateByCode()
    {
        var today = DateTime.UtcNow.Date;

        var taxRate = await Db.InsertAsync("TaxRate", new Dictionary<string, object?>
            { ["Code"] = "R15", ["EffectiveFrom"] = today, ["Rate"] = 0.15m, ["Tax"] = Db.NewId() });
        var taxCategory = await Db.InsertAsync("TaxCategory", new Dictionary<string, object?>
            { ["Code"] = "STD", ["Tax"] = Db.NewId(), ["Treatment"] = "Standard" });
        var taxCode = await Db.InsertAsync("TaxCode", new Dictionary<string, object?>
        {
            ["Code"] = "KSA-VAT", ["Name"] = "KSA VAT 15%", ["EffectiveFrom"] = today,
            ["Tax"] = Db.NewId(), ["TaxCategory"] = taxCategory, ["TaxRate"] = taxRate
        });

        var tax = GetService<ITaxService>();

        var rate = await tax.ResolveRateAsync((Guid)taxCode);
        Assert.IsTrue(rate == 0.15m, "ставка кода = 0.15, факт {0}", rate.HasValue ? rate.Value : -1m);

        var amount = await tax.CalculateByCodeAsync(200m, (Guid)taxCode);
        Assert.IsTrue(amount == 30m, "200 × 0.15 = 30, факт {0}", amount);
    }
}
