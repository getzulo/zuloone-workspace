using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Integration coverage for the Tax core: finalizing a tax calculation posts base and
// amount into the tax ledger, and the base×rate=amount guard rejects a mismatch.
public class TaxCalculationPostingTest : IntegrationTestScriptBase
{
    private async Task<(Guid LegalEntity, Guid Currency, Guid TaxCode, Guid Direction)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Saudi Riyal", ["Code"] = "SAR", ["Symbol"] = "﷼" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Saudi Arabia", ["CodeISO2"] = "SA", ["CodeISO3"] = "SAU", ["PhoneCode"] = "966" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME KSA", ["RegistrationNumber"] = "REG-TAX-1", ["Country"] = country, ["Currency"] = currency });

        var authority = await Db.InsertAsync("TaxAuthority", new Dictionary<string, object?>
            { ["Code"] = "ZATCA", ["Name"] = "ZATCA", ["CountryCode"] = "SA", ["IsActive"] = true });
        var type = await Db.InsertAsync("TaxType", new Dictionary<string, object?>
            { ["Code"] = "VAT", ["Name"] = "Value added tax", ["Category"] = "VAT" });
        var jurisdiction = await Db.InsertAsync("TaxJurisdiction", new Dictionary<string, object?>
            { ["Code"] = "SA", ["Name"] = "Saudi Arabia", ["CountryCode"] = "SA", ["Level"] = 0 });
        var from = new DateTime(2020, 1, 1);
        var tax = await Db.InsertAsync("Tax", new Dictionary<string, object?>
            { ["Code"] = "SA-VAT", ["Name"] = "Saudi VAT", ["TaxType"] = type, ["Authority"] = authority, ["Jurisdiction"] = jurisdiction, ["EffectiveFrom"] = from });
        var rate = await Db.InsertAsync("TaxRate", new Dictionary<string, object?>
            { ["Tax"] = tax, ["Code"] = "SA-VAT-15", ["Rate"] = 0.15m, ["EffectiveFrom"] = from });
        var category = await Db.InsertAsync("TaxCategory", new Dictionary<string, object?>
            { ["Tax"] = tax, ["Code"] = "STD", ["Treatment"] = "STANDARD" });
        var code = await Db.InsertAsync("TaxCode", new Dictionary<string, object?>
            { ["Code"] = "SA-VAT-15", ["Name"] = "Standard 15%", ["Tax"] = tax, ["TaxCategory"] = category, ["TaxRate"] = rate, ["EffectiveFrom"] = from });
        var direction = await Db.InsertAsync("TaxDirection", new Dictionary<string, object?>
            { ["Code"] = "OUTPUT", ["Name"] = "Output" });

        return (le, currency, code, direction);
    }

    private async Task<Guid> NewCalcAsync(
        (Guid LegalEntity, Guid Currency, Guid TaxCode, Guid Direction) s, decimal taxBase, decimal amount)
        => await Db.CreateDocumentAsync("TaxCalculation",
            new Dictionary<string, object?> { ["LegalEntity"] = s.LegalEntity, ["Currency"] = s.Currency, ["TaxPointDate"] = new DateTime(2026, 8, 20) },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["TaxCode"] = s.TaxCode, ["TaxBase"] = taxBase, ["RateValue"] = 0.15m,
                        ["TaxAmount"] = amount, ["Direction"] = s.Direction,
                    },
                },
            });

    [IntegrationTest("Финализация налогового расчёта разносит базу и сумму в TaxLedger")]
    public async Task FinalizePostsToLedger()
    {
        var s = await SetupAsync();
        var calc = await NewCalcAsync(s, taxBase: 100m, amount: 15m);

        await Db.ChangeSubtypeAsync("TaxCalculation", calc, "Finalized");

        var movements = await Db.QueryMovementsAsync("TaxLedger");
        Assert.IsTrue(movements.Count == 1, "ожидалось 1 движение TaxLedger, а не {0}", movements.Count);
        Assert.IsTrue(Convert.ToDecimal(movements[0]["TaxBase"]) == 100m, "база должна быть 100, а не {0}", movements[0]["TaxBase"]);
        Assert.IsTrue(Convert.ToDecimal(movements[0]["TaxAmount"]) == 15m, "сумма налога должна быть 15, а не {0}", movements[0]["TaxAmount"]);
    }

    [IntegrationTest("Расхождение база×ставка≠сумма отклоняется")]
    public async Task AmountMismatchIsRejected()
    {
        var s = await SetupAsync();
        var calc = await NewCalcAsync(s, taxBase: 100m, amount: 20m); // 100 × 0.15 = 15, не 20

        var rejected = false;
        try
        {
            await Db.ChangeSubtypeAsync("TaxCalculation", calc, "Finalized");
            rejected = (await Db.QueryMovementsAsync("TaxLedger")).Count == 0;
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "расчёт с неверной суммой налога должен быть отклонён");
    }
}
