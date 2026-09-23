using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Офіційна декларація ФОП 3 групи F0103309 (наказ Мінфіну 31.01.2025 № 57).
// Рядок 07 = 15 % (ПКУ 293.4). Рядок 23 = 1 % з доходу, не UA-VZ.
public class UkraineFopSingleTaxDeclarationFilingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static IUaTaxFiling Filing => GetService<IUaTaxFiling>();
    private static ITaxReturnService Returns => GetService<ITaxReturnService>();

    [IntegrationTest("F0103309: дохід 10000 за 5%, ЄП 500, ВЗ 100, до сплати 500")]
    public async Task PayableOnFivePercentWithinLimit()
    {
        var text = await ExportAsync(10000m, 500m, excessBase: 0m, excessTax: 0m);
        Assert.IsTrue(text.Contains("F0103309"), "тип форми. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("Не XML") || text.Contains("не XML"),
            "файл не видає себе за кабінет. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("06;Обсяг доходу за ставкою 5%;10000.00"),
            "рядок 06. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("07;Обсяг доходу за ставкою 15%;0.00"),
            "рядок 07 порожній. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("08;Усього доходу (р.05+р.06+р.07);10000.00"),
            "рядок 08. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("11;Сума єдиного податку 5%;500.00"),
            "рядок 11. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("12;Усього нараховано (р.09+р.10+р.11);500.00"),
            "рядок 12. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("13;Нараховано за попередній період (з 1 січня — нуль);0.00"),
            "рядок 13 у I кв. нуль. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("14;Усього до сплати (р.14.1+р.14.2);500.00"),
            "рядок 14. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("23;Військовий збір 1% з (р.05+р.06+р.07);100.00"),
            "рядок 23. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("25;ВЗ до сплати (рядок 23 − рядок 24);100.00"),
            "рядок 25. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("F0133109") && text.Contains("порожн"),
            "додатки не заповнюються. Факт:\n{0}", text);
    }

    [IntegrationTest("F0103309: UA-EP15 кладеться в рядок 07, не в форму ЮО")]
    public async Task ExcessFifteenPercentGoesToRow07()
    {
        var text = await ExportAsync(8000m, 400m, excessBase: 2000m, excessTax: 300m);
        Assert.IsTrue(text.Contains("06;Обсяг доходу за ставкою 5%;8000.00"),
            "рядок 06 у межах ліміту. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("07;Обсяг доходу за ставкою 15%;2000.00"),
            "15% — рядок 07. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("08;Усього доходу (р.05+р.06+р.07);10000.00"),
            "рядок 08. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("09;Сума єдиного податку 15%;300.00"),
            "рядок 09. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("11;Сума єдиного податку 5%;400.00"),
            "рядок 11. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("14;Усього до сплати (р.14.1+р.14.2);700.00"),
            "ЄП до сплати 700. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("23;Військовий збір 1% з (р.05+р.06+р.07);100.00"),
            "ВЗ з усього доходу. Факт:\n{0}", text);
    }

    [IntegrationTest("F0103309: півріччя — рядок 13 = I кв., рядок 14.1 = II кв.")]
    public async Task PreviousPeriodFromLedgerOnHalfYear()
    {
        var entity = await EntityAsync();
        var output = await OutputAsync();
        var five = await TaxCodeAsync("UA-EP5");
        await PostAsync(entity, five, output, new DateTime(2026, 3, 15), 10000m, 500m);
        await PostAsync(entity, five, output, new DateTime(2026, 6, 10), 4000m, 200m);

        var returnId = await Returns.BuildAsync(entity, new DateTime(2026, 1, 1), new DateTime(2026, 6, 30));
        await Filing.ExportAsync(returnId, "F0103309");
        var text = await PayloadAsync(entity);

        Assert.IsTrue(text.Contains("12;Усього нараховано (р.09+р.10+р.11);700.00"),
            "наростаючий підсумок. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("13;Нараховано за попередній період;500.00"),
            "I кв. з леджера. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("14.1;До сплати за період (рядок 12 − рядок 13);200.00"),
            "II кв. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("24;ВЗ за попередній період;100.00"),
            "ВЗ I кв. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("25;ВЗ до сплати (рядок 23 − рядок 24);40.00"),
            "ВЗ II кв. 140-100. Факт:\n{0}", text);
    }

    [IntegrationTest("F0103309: UaFopEsvAccrual 1760 → F0133109")]
    public async Task FopEsvAnnexFromDocument()
    {
        var entity = await EntityAsync();
        var output = await OutputAsync();
        var five = await TaxCodeAsync("UA-EP5");
        await PostAsync(entity, five, output, new DateTime(2026, 3, 15), 10000m, 500m);

        var docs = GetService<IDocumentManager>();
        var doc = await docs.NewDocumentAsync<UaFopEsvAccrual>();
        doc.LegalEntity = entity;
        doc.Amount = 1760m;
        doc.DocumentDate = new DateTime(2026, 3, 20);
        await docs.SaveDocumentAsync(doc);
        var commandId = await Db.FindCommandIdAsync("document", "UaPostFopEsv");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, doc.MetaId);
        Assert.IsTrue(run.Success, "нарахування ЄСВ ФОП: {0}", run.Message ?? "");

        var returnId = await Returns.BuildAsync(entity, new DateTime(2026, 1, 1), new DateTime(2026, 3, 31));
        await Filing.ExportAsync(returnId, "F0103309");
        var text = await PayloadAsync(entity);

        Assert.IsTrue(text.Contains("F0133109 ЄСВ за себе;1760.00"),
            "додаток з документа. Факт:\n{0}", text);
    }

    [IntegrationTest("F0103309: ділянка 100000 → рядок 14.2 = 1250")]
    public async Task MpzFromLandPlot()
    {
        var entity = await EntityAsync();
        var output = await OutputAsync();
        var five = await TaxCodeAsync("UA-EP5");
        await PostAsync(entity, five, output, new DateTime(2026, 3, 15), 10000m, 500m);

        var plot = Dict.NewRecord<UaLandPlot>();
        plot.Name = "Город";
        plot.LegalEntity = entity;
        plot.NormativeValue = 100000m;
        await Dict.SaveRecordAsync(plot);

        var returnId = await Returns.BuildAsync(entity, new DateTime(2026, 1, 1), new DateTime(2026, 3, 31));
        await Filing.ExportAsync(returnId, "F0103309");
        var text = await PayloadAsync(entity);

        Assert.IsTrue(text.Contains("14.2;Додаток 2 МПЗ;1250.00"),
            "рядок 14.2. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("14;Усього до сплати (р.14.1+р.14.2);1750.00"),
            "500 + 1250. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("F0133209 МПЗ;1250.00"),
            "додаток. Факт:\n{0}", text);
    }

    private async Task<string> PayloadAsync(Guid entity)
    {
        var rows = await GetService<IDictionaryManager<UaTaxFilingExport>>()
            .GetRecordsAsync($"LegalEntity = '{entity}'");
        var row = rows.FirstOrDefault(r => r.ReturnType == "F0103309");
        Assert.IsTrue(row != null, "рядок F0103309 має бути");
        return Convert.ToString(row!.Payload) ?? "";
    }

    private async Task<string> ExportAsync(
        decimal mappedBase, decimal mappedTax, decimal excessBase, decimal excessTax)
    {
        var entity = await EntityAsync();
        var output = await OutputAsync();
        var five = await TaxCodeAsync("UA-EP5");

        var day = new DateTime(2026, 3, 15);
        await PostAsync(entity, five, output, day, mappedBase, mappedTax);
        if (excessBase != 0m || excessTax != 0m)
        {
            var excess = await TaxCodeAsync("UA-EP15");
            await PostAsync(entity, excess, output, day, excessBase, excessTax);
        }

        var returnId = await Returns.BuildAsync(entity, new DateTime(2026, 1, 1), new DateTime(2026, 3, 31));
        await Filing.ExportAsync(returnId, "F0103309");
        return await PayloadAsync(entity);
    }

    private async Task<Guid> OutputAsync()
    {
        var rows = await Dict.GetRecordsAsync<TaxDirection>("Code = 'OUTPUT'", take: 2);
        Assert.IsTrue(rows.Count >= 1, "напрямок OUTPUT має бути в поставці");
        return rows[0].MetaId;
    }

    private async Task<Guid> TaxCodeAsync(string code)
    {
        var rows = await Dict.GetRecordsAsync<TaxCode>($"Code = '{code}'", take: 2);
        Assert.IsTrue(rows.Count == 1, "код «{0}» у поставці рівно один, знайдено {1}", code, rows.Count);
        return rows[0].MetaId;
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
        entity.Name = "ФОП ЄП";
        entity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        entity.TaxRegistrationNumber = "123456789012";
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        entity = await Dict.SaveRecordAsync(entity);
        return entity.MetaId;
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
