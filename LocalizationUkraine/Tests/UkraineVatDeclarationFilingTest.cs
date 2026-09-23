using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Офіційна декларація ПДВ J0200126 (наказ Мінфіну 09.08.2024 № 400).
// Рядки форми, не XML R00xG3. Рядок 20.2 (відшкодування) не підставляється.
public class UkraineVatDeclarationFilingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static IUaTaxFiling Filing => GetService<IUaTaxFiling>();
    private static ITaxReturnService Returns => GetService<ITaxReturnService>();

    [IntegrationTest("J0200126: 1.1 база 10000 ПДВ 2000, 10.1 кредит 1000, до сплати 1000")]
    public async Task PayableWhenLiabilitiesExceedCredit()
    {
        var text = await ExportAsync(10000m, 2000m, 5000m, 1000m);
        Assert.IsTrue(text.Contains("J0200126"), "тип форми. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("Не XML") || text.Contains("не XML"),
            "файл не видає себе за кабінет. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("1.1;Операції за основною ставкою 20%;10000.00;2000.00"),
            "рядок 1.1. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("10.1;Придбання зі ставкою 20%;5000.00;1000.00"),
            "рядок 10.1. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("9;Усього податкових зобов'язань (колонка Б);0.00;2000.00"),
            "рядок 9. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("17;Усього податкового кредиту (колонка Б);0.00;1000.00"),
            "рядок 17. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("18;До сплати (рядок 9 − рядок 17);0.00;1000.00"),
            "рядок 18. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("19;Від'ємне значення (рядок 17 − рядок 9). Не рядок 20.2;0.00;0.00"),
            "рядок 19 нуль. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("20.2") && text.Contains("порожн"),
            "відшкодування не підставляється. Факт:\n{0}", text);
    }

    [IntegrationTest("J0200126: UA-ZX рядок 2.1 експорт, UA-Z рядок 3 внутрішня нульова")]
    public async Task ZeroRatedRowsFromSeedCodes()
    {
        var entity = await EntityAsync();
        var output = await DirectionAsync("OUTPUT");
        var zx = (await Dict.GetRecordsAsync<TaxCode>("Code = 'UA-ZX'")).FirstOrDefault();
        var z = (await Dict.GetRecordsAsync<TaxCode>("Code = 'UA-Z'")).FirstOrDefault();
        Assert.IsTrue(zx != null, "код UA-ZX зобов'язаний бути в поставці");
        Assert.IsTrue(z != null, "код UA-Z зобов'язаний бути в поставці");

        var day = new DateTime(2026, 3, 15);
        await PostAsync(entity, zx!.MetaId, output, day, 8000m, 0m);
        await PostAsync(entity, z!.MetaId, output, day, 2000m, 0m);

        var returnId = await Returns.BuildAsync(entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportAsync(returnId, "J0200126");

        var rows = await GetService<IDictionaryManager<UaTaxFilingExport>>()
            .GetRecordsAsync($"LegalEntity = '{entity}'");
        var row = rows.FirstOrDefault(r => r.ReturnType == "J0200126");
        Assert.IsTrue(row != null, "рядок J0200126 має бути");
        var text = Convert.ToString(row!.Payload) ?? "";
        Assert.IsTrue(text.Contains("2.1;Експорт товарів (нульова ставка);8000.00;0.00"),
            "експорт UA-ZX. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("3;Операції за нульовою ставкою (крім експорту);2000.00;0.00"),
            "внутрішня нульова UA-Z. Факт:\n{0}", text);
    }

    [IntegrationTest("J0200126: кредит більший за зобов'язання — рядок 18 нуль, 19 = різниця")]
    public async Task NegativeValueWhenCreditExceeds()
    {
        var text = await ExportAsync(1000m, 200m, 5000m, 1000m);
        Assert.IsTrue(text.Contains("18;До сплати (рядок 9 − рядок 17);0.00;0.00"),
            "до сплати нуль. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("19;Від'ємне значення (рядок 17 − рядок 9). Не рядок 20.2;0.00;800.00"),
            "від'ємне 800. Факт:\n{0}", text);
    }

    [IntegrationTest("J0200126: UaVatRefund 500 при від'ємному 800 → рядок 20.2 = 500")]
    public async Task RefundClaimFillsRow202CappedAtNegative()
    {
        var entity = await EntityAsync();
        var output = await DirectionAsync("OUTPUT");
        var input = await DirectionAsync("INPUT");
        var code = await CodeAsync();
        await MapAsync(code, output, "1.1");
        await MapAsync(code, input, "10.1");

        var day = new DateTime(2026, 3, 15);
        await PostAsync(entity, code, output, day, 1000m, 200m);
        await PostAsync(entity, code, input, day, 5000m, 1000m);

        var docs = GetService<IDocumentManager>();
        var claim = await docs.NewDocumentAsync<UaVatRefund>();
        claim.LegalEntity = entity;
        claim.Amount = 500m;
        claim.DocumentDate = day;
        await docs.SaveDocumentAsync(claim);
        var commandId = await Db.FindCommandIdAsync("document", "UaPostVatRefund");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, claim.MetaId);
        Assert.IsTrue(run.Success, "заява на відшкодування: {0}", run.Message ?? "");

        var returnId = await Returns.BuildAsync(entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportAsync(returnId, "J0200126");
        var rows = await GetService<IDictionaryManager<UaTaxFilingExport>>()
            .GetRecordsAsync($"LegalEntity = '{entity}'");
        var row = rows.FirstOrDefault(r => r.ReturnType == "J0200126");
        Assert.IsTrue(row != null, "рядок J0200126 має бути");
        var text = Convert.ToString(row!.Payload) ?? "";

        Assert.IsTrue(text.Contains("19;Від'ємне значення (рядок 17 − рядок 9). Не рядок 20.2;0.00;800.00"),
            "від'ємне лишається. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("20.2;Бюджетне відшкодування (рядок 20.2);0.00;500.00"),
            "заявлена сума, не більше 19. Факт:\n{0}", text);
    }

    private async Task<string> ExportAsync(
        decimal outBase, decimal outVat, decimal inBase, decimal inVat)
    {
        var entity = await EntityAsync();
        var output = await DirectionAsync("OUTPUT");
        var input = await DirectionAsync("INPUT");
        var code = await CodeAsync();
        await MapAsync(code, output, "1.1");
        await MapAsync(code, input, "10.1");

        var day = new DateTime(2026, 3, 15);
        await PostAsync(entity, code, output, day, outBase, outVat);
        await PostAsync(entity, code, input, day, inBase, inVat);

        var returnId = await Returns.BuildAsync(entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportAsync(returnId, "J0200126");

        var rows = await GetService<IDictionaryManager<UaTaxFilingExport>>()
            .GetRecordsAsync($"LegalEntity = '{entity}'");
        var row = rows.FirstOrDefault(r => r.ReturnType == "J0200126");
        Assert.IsTrue(row != null, "рядок J0200126 має бути");
        return Convert.ToString(row!.Payload) ?? "";
    }

    private async Task<Guid> DirectionAsync(string code)
    {
        var rows = await Dict.GetRecordsAsync<TaxDirection>($"Code = '{code}'", take: 1);
        if (rows.Count > 0) return rows[0].MetaId;
        var d = Dict.NewRecord<TaxDirection>();
        d.Code = code;
        d.Name = code;
        return (await Dict.SaveRecordAsync(d)).MetaId;
    }

    private async Task<Guid> EntityAsync()
    {
        var currency = Dict.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = $"U{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "₴";
        currency = await Dict.SaveRecordAsync(currency);

        var country = Dict.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "380";
        country = await Dict.SaveRecordAsync(country);

        var entity = Dict.NewRecord<LegalEntity>();
        entity.Name = "ТОВ ПДВ";
        entity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        entity.TaxRegistrationNumber = "123456789012";
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        entity = await Dict.SaveRecordAsync(entity);
        return entity.MetaId;
    }

    private async Task<Guid> CodeAsync()
    {
        var from = new DateTime(2020, 1, 1);
        var uniq = $"{Db.NewId():N}"[..8];

        var authority = Dict.NewRecord<TaxAuthority>();
        authority.Code = $"AU-{uniq}";
        authority.Name = "DPS";
        authority.CountryCode = "UA";
        authority.IsActive = true;
        authority = await Dict.SaveRecordAsync(authority);

        var jurisdiction = Dict.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"JU-{uniq}";
        jurisdiction.Name = "Ukraine";
        jurisdiction.CountryCode = "UA";
        jurisdiction.Level = 0;
        jurisdiction = await Dict.SaveRecordAsync(jurisdiction);

        var tax = Dict.NewRecord<Tax>();
        tax.Code = $"T-{uniq}";
        tax.Name = "VAT";
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = from;
        tax = await Dict.SaveRecordAsync(tax);

        var category = Dict.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = $"C-{uniq}";
        category.Treatment = "STANDARD";
        category = await Dict.SaveRecordAsync(category);

        var rate = Dict.NewRecord<TaxRate>();
        rate.Tax = tax.MetaId;
        rate.TaxCategory = category.MetaId;
        rate.Code = $"R-{uniq}";
        rate.Rate = 0.20m;
        rate.EffectiveFrom = from;
        rate = await Dict.SaveRecordAsync(rate);

        var code = Dict.NewRecord<TaxCode>();
        code.Code = $"K-{uniq}";
        code.Name = "Standard 20%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        return (await Dict.SaveRecordAsync(code)).MetaId;
    }

    private async Task MapAsync(Guid code, Guid direction, string box)
    {
        var taxCode = await Dict.GetRecordAsync<TaxCode>(code);
        var m = Dict.NewRecord<TaxReportMapping>();
        m.Tax = taxCode!.Tax;
        m.TaxCode = code;
        m.Direction = direction;
        m.ReturnType = "J0200126";
        m.ReturnBox = box;
        m.EffectiveFrom = new DateTime(2020, 1, 1);
        await Dict.SaveRecordAsync(m);
    }

    private async Task PostAsync(Guid entity, Guid code, Guid direction, DateTime on,
                                        decimal taxBase, decimal amount)
        => await Db.PostMovementAsync("TaxLedger", on,
            new Dictionary<string, object?>
            {
                ["TaxCode"] = code,
                ["TaxDirection"] = direction,
                ["LegalEntity"] = entity,
            },
            new Dictionary<string, decimal>
            {
                ["TaxBase"] = taxBase,
                ["TaxAmount"] = amount,
                ["RecoverableAmount"] = amount,
            });
}
