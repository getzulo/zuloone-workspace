using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// TaxProfile attributes land on the rule context. TaxMapping pins a code
// only when no rule fired. A matching rule still wins.
public class TaxProfileMappingTest : IntegrationTestScriptBase
{
    private static ITaxService Svc => GetService<ITaxService>();
    private static IDocumentManager Documents => GetService<IDocumentManager>();

    private static readonly DateTime Origin = new(2020, 1, 1);
    private static readonly DateTime Today = new(2026, 6, 15);

    private string Uniq() => $"{Db.NewId():N}"[..8];

    [IntegrationTest("Атрибут профиля попадает в контекст и срабатывает правило")]
    public async Task ProfileAttributeFeedsRule()
    {
        var fallback = await NewTaxCodeAsync(0.05m);
        await SetDefaultAsync((await RecordAsync<TaxCode>(fallback))!.Code);

        var exempt = await NewTaxCodeAsync(0m);
        var rule = await NewRuleAsync(exempt, 10);
        await NewConditionAsync(rule, "buyer.profile.Exempt", TaxRuleOperator.Eq, "yes");

        var party = Db.NewId();
        var profile = await NewProfileAsync("Customer", party);
        await NewAttributeAsync(profile, "Exempt", "yes");

        await EnsureOutputDirectionAsync();
        var le = await NewLegalEntityAsync();
        var calcId = await Svc.CreateCalculationAsync(
            le, "OUTPUT", 1000m, $"Prof {Uniq()}", Today,
            Ctx(("buyer.id", party)));
        Assert.IsNotNull(calcId, "расчёт создан");
        var calc = await Documents.GetDocumentAsync<TaxCalculation>(calcId!.Value);
        Assert.IsTrue(calc!.MatchedRule == rule, "правило сработало по профилю");
        Assert.IsTrue(calc.Lines[0].TaxCode == exempt, "код из правила, не умолчание");
        Assert.IsTrue(calc.Lines[0].TaxAmount == 0m, "освобождение 0, факт {0}", calc.Lines[0].TaxAmount);
    }

    [IntegrationTest("Сопоставление бьёт умолчание, когда правил нет")]
    public async Task MappingOverridesDefaultWhenNoRule()
    {
        var fallback = await NewTaxCodeAsync(0.05m);
        await SetDefaultAsync((await RecordAsync<TaxCode>(fallback))!.Code);
        var mapped = await NewTaxCodeAsync(0.20m);

        var party = Db.NewId();
        await NewMappingAsync("Customer", party, mapped, 10);

        await EnsureOutputDirectionAsync();
        var le = await NewLegalEntityAsync();
        var calcId = await Svc.CreateCalculationAsync(
            le, "OUTPUT", 1000m, $"Map {Uniq()}", Today,
            Ctx(("buyer.id", party)));
        Assert.IsNotNull(calcId, "расчёт создан");
        var calc = await Documents.GetDocumentAsync<TaxCalculation>(calcId!.Value);
        Assert.IsTrue(calc!.MatchedRule == Guid.Empty, "правила не было");
        Assert.IsTrue(calc.Lines[0].TaxCode == mapped, "код из сопоставления");
        Assert.IsTrue(calc.Lines[0].TaxAmount == 200m, "1000 × 20%, факт {0}", calc.Lines[0].TaxAmount);
    }

    [IntegrationTest("Сработавшее правило бьёт сопоставление")]
    public async Task RuleBeatsMapping()
    {
        var fallback = await NewTaxCodeAsync(0.05m);
        await SetDefaultAsync((await RecordAsync<TaxCode>(fallback))!.Code);
        var mapped = await NewTaxCodeAsync(0.20m);
        var ruled = await NewTaxCodeAsync(0.10m);

        var party = Db.NewId();
        await NewMappingAsync("Customer", party, mapped, 10);
        var rule = await NewRuleAsync(ruled, 10);
        await NewConditionAsync(rule, "buyer.type", TaxRuleOperator.Eq, "B2B");

        await EnsureOutputDirectionAsync();
        var le = await NewLegalEntityAsync();
        var calcId = await Svc.CreateCalculationAsync(
            le, "OUTPUT", 1000m, $"Both {Uniq()}", Today,
            Ctx(("buyer.id", party), ("buyer.type", "B2B")));
        var calc = await Documents.GetDocumentAsync<TaxCalculation>(calcId!.Value);
        Assert.IsTrue(calc!.Lines[0].TaxCode == ruled, "правило выше сопоставления");
        Assert.IsTrue(calc.Lines[0].TaxAmount == 100m, "1000 × 10%, факт {0}", calc.Lines[0].TaxAmount);
    }

    [IntegrationTest("Номенклатура бьёт клиента в сопоставлении")]
    public async Task ItemMappingBeatsCustomerMapping()
    {
        var fallback = await NewTaxCodeAsync(0.05m);
        await SetDefaultAsync((await RecordAsync<TaxCode>(fallback))!.Code);
        var customerCode = await NewTaxCodeAsync(0.10m);
        var itemCode = await NewTaxCodeAsync(0.20m);

        var party = Db.NewId();
        var item = Db.NewId();
        await NewMappingAsync("Customer", party, customerCode, 1);
        await NewMappingAsync("Item", item, itemCode, 50);

        await EnsureOutputDirectionAsync();
        var le = await NewLegalEntityAsync();
        var calcId = await Svc.CreateCalculationAsync(
            le, "OUTPUT", 1000m, $"Spec {Uniq()}", Today,
            Ctx(("buyer.id", party), ("item.id", item)));
        var calc = await Documents.GetDocumentAsync<TaxCalculation>(calcId!.Value);
        Assert.IsTrue(calc!.Lines[0].TaxCode == itemCode, "Item специфичнее Customer");
    }

    [IntegrationTest("Пересечение профилей одной стороны отклоняется")]
    public async Task OverlappingProfileIsRejected()
    {
        var party = Db.NewId();
        await NewProfileAsync("Customer", party);
        var clash = GetService<IDictionaryManager>().NewRecord<TaxProfile>();
        clash.Name = "Clash";
        clash.PartyType = "Customer";
        clash.PartyId = party;
        clash.EffectiveFrom = new DateTime(2026, 1, 1);
        var reason = string.Empty;
        try { await GetService<IDictionaryManager>().SaveRecordAsync(clash); }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("уже есть профиль"),
            "пересечение профилей обязано быть отклонено, факт: {0}", reason);
    }

    [IntegrationTest("Пересечение сопоставлений одного источника отклоняется")]
    public async Task OverlappingMappingIsRejected()
    {
        var source = Db.NewId();
        var code = await NewTaxCodeAsync(0.15m);
        await NewMappingAsync("Customer", source, code, 10);
        var clash = GetService<IDictionaryManager>().NewRecord<TaxMapping>();
        clash.SourceType = "Customer";
        clash.SourceId = source;
        clash.TaxCode = code;
        clash.Priority = 20;
        clash.EffectiveFrom = new DateTime(2026, 1, 1);
        var reason = string.Empty;
        try { await GetService<IDictionaryManager>().SaveRecordAsync(clash); }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("уже есть сопоставление"),
            "пересечение сопоставлений обязано быть отклонено, факт: {0}", reason);
    }

    private async Task<Guid> NewTaxCodeAsync(decimal rate)
    {
        var uniq = Uniq();
        var taxId = await NewRecordAsync<Tax>(t =>
        {
            t.Code = $"T-{uniq}";
            t.Name = "Profile tax";
            t.Authority = Db.NewId();
            t.Jurisdiction = Db.NewId();
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
            c.Code = $"STD-{uniq}";
            c.Treatment = rate == 0m ? "EXEMPT" : "STANDARD";
        });
        return await NewRecordAsync<TaxCode>(c =>
        {
            c.Code = $"VAT-{uniq}";
            c.Name = "Profile code";
            c.Tax = taxId;
            c.TaxCategory = category;
            c.TaxRate = rateId;
            c.EffectiveFrom = Origin;
        });
    }

    private async Task<Guid> NewRuleAsync(Guid taxCode, int priority)
        => await NewRecordAsync<TaxRule>(r =>
        {
            r.Code = $"RULE-{Uniq()}";
            r.Name = "Rule";
            r.Priority = priority;
            r.TaxCode = taxCode;
            r.EffectiveFrom = Origin;
        });

    private async Task NewConditionAsync(Guid rule, string field, TaxRuleOperator op, string value)
        => await NewRecordAsync<TaxRuleCondition>(c =>
        {
            c.TaxRule = rule;
            c.Field = field;
            c.Operator = op;
            c.Value = value;
            c.ConditionGroup = 0;
        });

    private async Task<Guid> NewProfileAsync(string partyType, Guid partyId)
        => await NewRecordAsync<TaxProfile>(p =>
        {
            p.Name = $"P-{Uniq()}";
            p.PartyType = partyType;
            p.PartyId = partyId;
            p.EffectiveFrom = Origin;
        });

    private Task NewAttributeAsync(Guid profile, string code, string value)
        => NewRecordAsync<TaxProfileAttribute>(a =>
        {
            a.TaxProfile = profile;
            a.AttributeCode = code;
            a.Value = value;
        });

    private Task NewMappingAsync(string sourceType, Guid sourceId, Guid taxCode, int priority)
        => NewRecordAsync<TaxMapping>(m =>
        {
            m.SourceType = sourceType;
            m.SourceId = sourceId;
            m.TaxCode = taxCode;
            m.Priority = priority;
            m.EffectiveFrom = Origin;
        });

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

    private async Task<Guid> NewLegalEntityAsync()
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
            le.Country = country;
            le.Currency = currency;
        });
    }

    private static Dictionary<string, object?> Ctx(params (string Key, object? Value)[] pairs)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }
}
