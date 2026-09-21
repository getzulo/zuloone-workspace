using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// ═══ ВЫГРУЗКА ДЕКЛАРАЦИИ ДЛЯ ПОДАЧИ ═════════════════════════════════════════
//
// Бухгалтеры подают ДВУМЯ способами: ТОВ через M.E.Doc, ФОПов через кабинет
// банка, и часть цифр вбивают руками. Печатная форма отвечает на «покажи», файл
// — на «перенеси, ничего не пересчитывая». Проверяется именно второе.
//
// Формат нейтральный и схемой ДПС не притворяется: файл, похожий на официальный
// и им не являющийся, хуже отсутствия файла — его отнесут в кабинет и получат
// отказ.
public class UkraineFilingExportTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITaxReturnService Returns => GetService<ITaxReturnService>();
    private static IUaTaxFiling Filing => GetService<IUaTaxFiling>();

    private const string ReturnType = "VAT-UA-TEST";

    private string Uniq() => $"{Db.NewId():N}"[..8];

    private async Task<Guid> DirectionAsync(string code)
    {
        var rows = await DictionaryManager.GetRecordsAsync<TaxDirection>($"Code = '{code}'", take: 1);
        if (rows.Count > 0) return rows[0].MetaId;
        var d = DictionaryManager.NewRecord<TaxDirection>();
        d.Code = code;
        d.Name = code;
        return (await DictionaryManager.SaveRecordAsync(d)).MetaId;
    }

    private async Task<LegalEntity> EntityAsync(string taxNumber)
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = $"U{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "₴";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "380";
        country = await DictionaryManager.SaveRecordAsync(country);

        var entity = DictionaryManager.NewRecord<LegalEntity>();
        entity.Name = "Kyiv Trading";
        entity.RegistrationNumber = $"REG-EXP-{Db.NewId():N}"[..16];
        entity.TaxRegistrationNumber = taxNumber;
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        return await DictionaryManager.SaveRecordAsync(entity);
    }

    private async Task<Guid> CodeAsync(string name)
    {
        var from = new DateTime(2020, 1, 1);
        var uniq = Uniq();

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"AU-{uniq}";
        authority.Name = "DPS";
        authority.CountryCode = "UA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"JU-{uniq}";
        jurisdiction.Name = "Ukraine";
        jurisdiction.CountryCode = "UA";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"T-{uniq}";
        tax.Name = name;
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = from;
        tax = await DictionaryManager.SaveRecordAsync(tax);

        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = $"C-{uniq}";
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var rate = DictionaryManager.NewRecord<TaxRate>();
        rate.Tax = tax.MetaId;
        rate.TaxCategory = category.MetaId;
        rate.Code = $"R-{uniq}";
        rate.Rate = 0.20m;
        rate.EffectiveFrom = from;
        rate = await DictionaryManager.SaveRecordAsync(rate);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"K-{uniq}";
        code.Name = name;
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        return (await DictionaryManager.SaveRecordAsync(code)).MetaId;
    }

    private async Task MapAsync(Guid code, Guid direction, string box)
    {
        var taxCode = await DictionaryManager.GetRecordAsync<TaxCode>(code);
        var m = DictionaryManager.NewRecord<TaxReportMapping>();
        m.Tax = taxCode!.Tax;
        m.TaxCode = code;
        m.Direction = direction;
        m.ReturnType = ReturnType;
        m.ReturnBox = box;
        m.EffectiveFrom = new DateTime(2020, 1, 1);
        await DictionaryManager.SaveRecordAsync(m);
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

    private static async Task<List<UaTaxFilingExport>> ExportsAsync(Guid entity)
        => await GetService<IDictionaryManager<UaTaxFilingExport>>()
            .GetRecordsAsync($"LegalEntity = '{entity}'");

    [IntegrationTest("Выгрузка несёт податковий номер, период и итоги ПО ЯЧЕЙКАМ")]
    public async Task PayloadCarriesHeaderAndBoxTotals()
    {
        var entity = await EntityAsync("123456789012");
        var output = await DirectionAsync("OUTPUT");
        var code = await CodeAsync("Standard 20%");
        await MapAsync(code, output, "1.1");

        var day = new DateTime(2026, 3, 15);
        // ДВА движения одного кода: в файл обязан попасть ИТОГ рядка, а не две
        // строки — в декларацию переносят итог, а не каждую проводку.
        await PostAsync(entity.MetaId, code, output, day, 1000m, 200m);
        await PostAsync(entity.MetaId, code, output, day, 500m, 100m);

        var returnId = await Returns.BuildAsync(entity.MetaId, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        var exportId = await Filing.ExportAsync(returnId, ReturnType);
        Assert.IsNotNull(exportId, "выгрузка должна создаться");

        var rows = await ExportsAsync(entity.MetaId);
        Assert.IsTrue(rows.Count == 1, "одна выгрузка, факт {0}", rows.Count);
        var text = Convert.ToString(rows[0].Payload) ?? "";

        Assert.IsTrue(text.Contains("123456789012"),
            "в шапке обязан быть податковий номер, чтобы не искать его в другом окне");
        Assert.IsTrue(text.Contains("2026-03-01") && text.Contains("2026-03-31"),
            "в шапке обязан быть период");
        Assert.IsTrue(text.Contains("1.1;1500.00;300.00"),
            "рядок 1.1 сложен: база 1500, налог 300. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("не схема ДПС"),
            "файл обязан честно называть себя нейтральным, а не выдавать себя за официальный");
    }

    [IntegrationTest("Код без сопоставления виден отдельной строкой, а не теряется")]
    public async Task UnmappedCodeIsVisibleNotDropped()
    {
        var entity = await EntityAsync("222222222222");
        var output = await DirectionAsync("OUTPUT");
        var mapped = await CodeAsync("Mapped");
        var orphan = await CodeAsync("Unmapped");
        await MapAsync(mapped, output, "1.1");
        // orphan намеренно БЕЗ сопоставления.

        var day = new DateTime(2026, 4, 10);
        await PostAsync(entity.MetaId, mapped, output, day, 100m, 20m);
        await PostAsync(entity.MetaId, orphan, output, day, 700m, 140m);

        var returnId = await Returns.BuildAsync(entity.MetaId, new DateTime(2026, 4, 1), new DateTime(2026, 4, 30));
        await Filing.ExportAsync(returnId, ReturnType);

        var text = Convert.ToString((await ExportsAsync(entity.MetaId))[0].Payload) ?? "";

        // Молча укороченный файл читается как «в декларации меньше», и ошибку
        // заметят уже после подачи.
        Assert.IsTrue(text.Contains("(не зіставлено);700.00;140.00"),
            "несопоставленный код обязан быть ВИДЕН, а не выброшен. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("1.1;100.00;20.00"),
            "сопоставленный рядок на месте. Факт:\n{0}", text);
    }

    [IntegrationTest("Повторная выгрузка за тот же период замещает, а не плодит")]
    public async Task ReExportReplaces()
    {
        var entity = await EntityAsync("333333333333");
        var output = await DirectionAsync("OUTPUT");
        var code = await CodeAsync("Standard 20%");
        await MapAsync(code, output, "1.1");

        var day = new DateTime(2026, 5, 20);
        await PostAsync(entity.MetaId, code, output, day, 100m, 20m);

        var from = new DateTime(2026, 5, 1);
        var to = new DateTime(2026, 5, 31);
        await Filing.ExportAsync(await Returns.BuildAsync(entity.MetaId, from, to), ReturnType);

        // Декларацию пересобирают — уточнили период, доначислили, поправили
        // сопоставление. Две выгрузки за один период это приглашение подать старую.
        await PostAsync(entity.MetaId, code, output, day, 900m, 180m);
        await Filing.ExportAsync(await Returns.BuildAsync(entity.MetaId, from, to), ReturnType);

        var rows = await ExportsAsync(entity.MetaId);
        Assert.IsTrue(rows.Count == 1, "выгрузка за период одна, факт {0}", rows.Count);
        Assert.IsTrue((Convert.ToString(rows[0].Payload) ?? "").Contains("1.1;1000.00;200.00"),
            "в выгрузке свежие цифры 1000/200. Факт:\n{0}", rows[0].Payload);
    }
}
