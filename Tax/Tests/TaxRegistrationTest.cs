using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Dated VAT number lives in TaxRegistration, not only on the party card.
// The card is the print fallback. Exemption reasons are a core catalog:
// EXEMPT/OUT_OF_SCOPE lines copy TaxCode.ExemptionReason; ZERO_RATED does not.
public class TaxRegistrationTest : IntegrationTestScriptBase
{
    private static ITaxService Svc => GetService<ITaxService>();
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager Documents => GetService<IDocumentManager>();

    private static readonly DateTime Origin = new(2020, 1, 1);
    private static readonly DateTime Today = new(2026, 6, 15);

    private string Uniq() => $"{Db.NewId():N}"[..8];

    [IntegrationTest("Регистрация на дату бьёт номер с карточки юрлица")]
    public async Task RegistrationBeatsPartyCardOnCalculation()
    {
        var fx = await ContourAsync(0.15m, "STANDARD");
        var le = await NewLegalEntityAsync("310000000000003");
        await NewRegistrationAsync(le, fx.TaxId, fx.Jurisdiction, "311111111111113", Origin);

        var number = await Svc.ResolveRegistrationNumberAsync("LegalEntity", le, fx.TaxId, Today);
        Assert.IsTrue(number == "311111111111113",
            "действующая регистрация, не карточка, факт {0}", number);

        await SetDefaultAsync(fx.Code);
        await EnsureOutputDirectionAsync();
        var calcId = await Svc.CreateCalculationAsync(
            le, "OUTPUT", 1000m, $"Reg {Uniq()}", Today);
        Assert.IsNotNull(calcId, "расчёт создан");
        var calc = await Documents.GetDocumentAsync<TaxCalculation>(calcId!.Value);
        Assert.IsTrue(calc!.RegistrationNumber == "311111111111113",
            "расчёт помнит номер на дату события, факт {0}", calc.RegistrationNumber);
    }

    [IntegrationTest("Истёкшая регистрация откатывается на карточку")]
    public async Task ExpiredRegistrationFallsBackToCard()
    {
        var fx = await ContourAsync(0.15m, "STANDARD");
        var le = await NewLegalEntityAsync("310000000000003");
        await NewRegistrationAsync(le, fx.TaxId, fx.Jurisdiction, "311111111111113",
            Origin, new DateTime(2025, 12, 31));

        var number = await Svc.ResolveRegistrationNumberAsync("LegalEntity", le, fx.TaxId, Today);
        Assert.IsTrue(number == "310000000000003",
            "окно закрыто — карточка, факт {0}", number);
    }

    [IntegrationTest("Пересечение окон одной стороны отклоняется при вводе")]
    public async Task OverlappingRegistrationIsRejected()
    {
        var fx = await ContourAsync(0.15m, "STANDARD");
        var le = await NewLegalEntityAsync("310000000000003");
        await NewRegistrationAsync(le, fx.TaxId, fx.Jurisdiction, "311111111111113", Origin);

        var clash = DictionaryManager.NewRecord<TaxRegistration>();
        clash.PartyType = "LegalEntity";
        clash.PartyId = le;
        clash.Tax = fx.TaxId;
        clash.Jurisdiction = fx.Jurisdiction;
        clash.RegistrationNumber = "322222222222223";
        clash.ValidFrom = new DateTime(2026, 1, 1);

        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(clash); }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("уже есть регистрация"),
            "пересечение обязано быть отклонено на вводе, факт: {0}", reason);

        var found = await Svc.FindOverlappingRegistrationAsync(
            "LegalEntity", le, fx.TaxId, fx.Jurisdiction, Guid.Empty,
            new DateTime(2026, 1, 1), null);
        Assert.IsNotNull(found, "сервис видит то же пересечение, что и обработчик");
    }

    [IntegrationTest("Чужой PartyType не подменяет номер юрлица")]
    public async Task CustomerRegistrationDoesNotUseLegalEntityCard()
    {
        var fx = await ContourAsync(0.15m, "STANDARD");
        var le = await NewLegalEntityAsync("310000000000003");
        var customerId = Db.NewId();
        var number = await Svc.ResolveRegistrationNumberAsync("Customer", customerId, fx.TaxId, Today);
        Assert.IsTrue(string.IsNullOrEmpty(number),
            "у покупателя без строки нет фолбэка на юрлицо, факт {0}", number);

        await NewRegistrationAsync(customerId, fx.TaxId, fx.Jurisdiction, "300000000000003",
            Origin, null, "Customer");
        number = await Svc.ResolveRegistrationNumberAsync("Customer", customerId, fx.TaxId, Today);
        Assert.IsTrue(number == "300000000000003",
            "номер покупателя из справочника, факт {0}", number);
        Assert.IsTrue(
            await Svc.ResolveRegistrationNumberAsync("LegalEntity", le, fx.TaxId, Today) == "310000000000003",
            "карточка юрлица не задета регистрацией покупателя");
    }

    [IntegrationTest("Освобождённый код штампует причину, нулевой — нет")]
    public async Task ExemptCodeStampsReasonZeroRatedDoesNot()
    {
        var exempt = await ContourAsync(0m, "EXEMPT");
        var reasonId = await NewRecordAsync<TaxExemptionReason>(r =>
        {
            r.Tax = exempt.TaxId;
            r.Code = $"E-{Uniq()}";
            r.Name = "Financial services";
            r.EffectiveFrom = Origin;
        });
        var exemptCode = await RecordAsync<TaxCode>(exempt.CodeId);
        exemptCode!.ExemptionReason = reasonId;
        await GetService<IDictionaryManager<TaxCode>>().SaveRecordAsync(exemptCode);

        var zero = await ContourAsync(0m, "ZERO_RATED");
        var zeroReason = await NewRecordAsync<TaxExemptionReason>(r =>
        {
            r.Tax = zero.TaxId;
            r.Code = $"Z-{Uniq()}";
            r.Name = "Should not stamp";
            r.EffectiveFrom = Origin;
        });
        var zeroCode = await RecordAsync<TaxCode>(zero.CodeId);
        zeroCode!.ExemptionReason = zeroReason;
        await GetService<IDictionaryManager<TaxCode>>().SaveRecordAsync(zeroCode);

        await EnsureOutputDirectionAsync();
        var le = await NewLegalEntityAsync("310000000000003");

        await SetDefaultAsync(exemptCode.Code);
        var exemptCalcId = await Svc.CreateCalculationAsync(
            le, "OUTPUT", 1000m, $"Exempt {Uniq()}", Today);
        var exemptCalc = await Documents.GetDocumentAsync<TaxCalculation>(exemptCalcId!.Value);
        Assert.IsTrue(exemptCalc!.Lines[0].TaxAmount == 0m, "освобождение — ноль");
        Assert.IsTrue(exemptCalc.Lines[0].ExemptionReason == reasonId,
            "EXEMPT копирует причину с кода, факт {0}", exemptCalc.Lines[0].ExemptionReason);

        await SetDefaultAsync(zeroCode.Code);
        var zeroCalcId = await Svc.CreateCalculationAsync(
            le, "OUTPUT", 1000m, $"Zero {Uniq()}", Today);
        var zeroCalc = await Documents.GetDocumentAsync<TaxCalculation>(zeroCalcId!.Value);
        Assert.IsTrue(zeroCalc!.Lines[0].TaxAmount == 0m, "нулевая ставка — ноль");
        Assert.IsTrue(zeroCalc.Lines[0].ExemptionReason == Guid.Empty,
            "ZERO_RATED причину не несёт, факт {0}", zeroCalc.Lines[0].ExemptionReason);
    }

    [IntegrationTest("Код не принимает причину чужого налога")]
    public async Task ForeignExemptionReasonIsRejected()
    {
        var a = await ContourAsync(0m, "EXEMPT");
        var b = await ContourAsync(0m, "EXEMPT");
        var reasonOfB = await NewRecordAsync<TaxExemptionReason>(r =>
        {
            r.Tax = b.TaxId;
            r.Code = $"X-{Uniq()}";
            r.Name = "Other tax";
            r.EffectiveFrom = Origin;
        });

        var code = await RecordAsync<TaxCode>(a.CodeId);
        code!.ExemptionReason = reasonOfB;
        var text = string.Empty;
        try { await GetService<IDictionaryManager<TaxCode>>().SaveRecordAsync(code); }
        catch (Exception ex) { text = ex.Message; }
        Assert.IsTrue(text.Contains("другому налогу"),
            "причина чужого налога отклоняется, факт: {0}", text);
    }

    private sealed class Fixture
    {
        public Guid TaxId;
        public Guid CodeId;
        public string Code = "";
        public Guid Jurisdiction;
    }

    private async Task<Fixture> ContourAsync(decimal rate, string treatment)
    {
        var uniq = Uniq();
        var jurisdiction = Db.NewId();
        var taxId = await NewRecordAsync<Tax>(t =>
        {
            t.Code = $"T-{uniq}";
            t.Name = "Registration tax";
            t.Authority = Db.NewId();
            t.Jurisdiction = jurisdiction;
            t.EffectiveFrom = Origin;
        });
        var rateId = await NewRecordAsync<TaxRate>(r =>
        {
            r.Tax = taxId;
            r.Code = $"R-{uniq}";
            r.Rate = rate;
            r.EffectiveFrom = Origin;
        });
        var category = await NewRecordAsync<TaxCategory>(c =>
        {
            c.Tax = taxId;
            c.Code = $"{treatment[..3]}-{uniq}";
            c.Treatment = treatment;
        });
        var codeId = await NewRecordAsync<TaxCode>(c =>
        {
            c.Code = $"VAT-{uniq}";
            c.Name = treatment;
            c.Tax = taxId;
            c.TaxCategory = category;
            c.TaxRate = rateId;
            c.EffectiveFrom = Origin;
        });
        return new Fixture
        {
            TaxId = taxId,
            CodeId = codeId,
            Code = $"VAT-{uniq}",
            Jurisdiction = jurisdiction,
        };
    }

    private async Task NewRegistrationAsync(
        Guid partyId, Guid taxId, Guid jurisdiction, string number,
        DateTime from, DateTime? to = null, string partyType = "LegalEntity")
        => await NewRecordAsync<TaxRegistration>(r =>
        {
            r.PartyType = partyType;
            r.PartyId = partyId;
            r.Tax = taxId;
            r.Jurisdiction = jurisdiction;
            r.RegistrationNumber = number;
            r.ValidFrom = from;
            r.ValidTo = to;
        });

    private async Task<Guid> NewLegalEntityAsync(string vat)
    {
        var currency = await NewRecordAsync<Currency>(c =>
        {
            c.Name = "Euro";
            c.Code = "EUR";
            c.Symbol = "€";
        });
        var country = await NewRecordAsync<Country>(c =>
        {
            c.Name = "Germany";
            c.CodeISO2 = "DE";
            c.CodeISO3 = "DEU";
            c.PhoneCode = "49";
        });
        return await NewRecordAsync<LegalEntity>(le =>
        {
            le.Name = "ACME GmbH";
            le.RegistrationNumber = $"REG-{Uniq()}";
            le.TaxRegistrationNumber = vat;
            le.Country = country;
            le.Currency = currency;
        });
    }

    private static async Task SetDefaultAsync(string code)
    {
        var manager = GetService<IDictionaryManager<TaxSettings>>();
        var rows = await RecordsAsync<TaxSettings>(null);
        var settings = rows.Count > 0 ? rows[0] : await manager.NewRecordAsync();
        settings.DefaultTaxCode = code;
        settings.PricesIncludeTax = false;
        await manager.SaveRecordAsync(settings);
    }

    private async Task EnsureOutputDirectionAsync()
    {
        var existing = await RecordsAsync<TaxDirection>("Code = 'OUTPUT'");
        if (existing.Count > 0) return;
        await NewRecordAsync<TaxDirection>(d =>
        {
            d.Code = "OUTPUT";
            d.Name = "Output";
        });
    }
}
