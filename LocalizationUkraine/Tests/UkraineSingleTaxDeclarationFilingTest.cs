using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Офіційна декларація ЮО 3 групи J0103509 (наказ Мінфіну 31.01.2025 № 57).
// Коди — з поставки vat-UA, не зібрані тестом. UA-EP15 не рядок 2.
public class UkraineSingleTaxDeclarationFilingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static IUaTaxFiling Filing => GetService<IUaTaxFiling>();
    private static ITaxReturnService Returns => GetService<ITaxReturnService>();

    [IntegrationTest("J0103509: дохід 10000 за 5%, податок 500, до сплати 500, рядок 9 нуль у I кв.")]
    public async Task PayableOnFivePercentWithinLimit()
    {
        var text = await ExportAsync(10000m, 500m, excessBase: 0m, excessTax: 0m);
        Assert.IsTrue(text.Contains("J0103509"), "тип форми. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("Не XML") || text.Contains("не XML"),
            "файл не видає себе за кабінет. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("1;Обсяг доходу за основною ставкою;0.00;10000.00"),
            "рядок 1 графа 4. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("5;Усього доходу (р.1+р.2+р.3+р.4);0.00;10000.00"),
            "рядок 5. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("6;Сума єдиного податку (р.1 × ставка);0.00;500.00"),
            "рядок 6. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("8;Усього нараховано (р.6+р.7);0.00;500.00"),
            "рядок 8. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("9;Нараховано за попередній період (з 1 січня — нуль);0.00;0.00"),
            "рядок 9 у I кв. нуль. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("10;До сплати за період (рядок 8 − рядок 9);0.00;500.00"),
            "рядок 10. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("2;Дохід понад ліміт (подвійна ставка). Не UA-EP15;0.00;0.00"),
            "рядок 2 порожній. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("МПЗ") && text.Contains("порожн"),
            "додаток МПЗ не заповнюється. Факт:\n{0}", text);
    }

    [IntegrationTest("J0103509: UA-EP15 не кладеться в рядок 2 ЮО")]
    public async Task ExcessFifteenPercentStaysUnmapped()
    {
        var text = await ExportAsync(8000m, 400m, excessBase: 2000m, excessTax: 300m);
        Assert.IsTrue(text.Contains("1;Обсяг доходу за основною ставкою;0.00;8000.00"),
            "рядок 1 лише в межах ліміту. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("2;Дохід понад ліміт (подвійна ставка). Не UA-EP15;0.00;0.00"),
            "15% не рядок 2. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("10;До сплати за період (рядок 8 − рядок 9);0.00;400.00"),
            "до сплати без 15%. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("(не зіставлено)") && text.Contains("2000.00;300.00"),
            "перевищення видно окремо. Факт:\n{0}", text);
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
        await Filing.ExportAsync(returnId, "J0103509");

        var rows = await GetService<IDictionaryManager<UaTaxFilingExport>>()
            .GetRecordsAsync($"LegalEntity = '{entity}'");
        var row = rows.FirstOrDefault(r => r.ReturnType == "J0103509");
        Assert.IsTrue(row != null, "рядок J0103509 має бути");
        return Convert.ToString(row!.Payload) ?? "";
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
        entity.Name = "ТОВ ЄП";
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
