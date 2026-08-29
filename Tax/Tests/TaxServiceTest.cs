using System;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
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
        // Коды уникальны в справочнике: фиксированные значения ломали бы тест на
        // стенде, где такая запись уже заведена.
        var uniq = $"{Db.NewId():N}"[..6];

        var taxRate = await NewRecordAsync<TaxRate>(r =>
        {
            r.Code = $"R15-{uniq}";
            r.EffectiveFrom = today;
            r.Rate = 0.15m;
            r.Tax = Db.NewId();
        });
        var taxCategory = await NewRecordAsync<TaxCategory>(c =>
        {
            c.Code = $"STD-{uniq}";
            c.Tax = Db.NewId();
            c.Treatment = "Standard";
        });
        var taxCode = await NewRecordAsync<TaxCode>(c =>
        {
            c.Code = $"VAT-{uniq}";
            c.Name = "KSA VAT 15%";
            c.EffectiveFrom = today;
            c.Tax = Db.NewId();
            c.TaxCategory = taxCategory;
            c.TaxRate = taxRate;
        });

        var tax = GetService<ITaxService>();

        var rate = await tax.ResolveRateAsync(taxCode);
        Assert.IsTrue(rate == 0.15m, "ставка кода = 0.15, факт {0}", rate.HasValue ? rate.Value : -1m);

        var amount = await tax.CalculateByCodeAsync(200m, taxCode);
        Assert.IsTrue(amount == 30m, "200 × 0.15 = 30, факт {0}", amount);
    }
}
