using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

// Налоговый код по умолчанию — ссылка. Движок читает строку DefaultTaxCode,
// поэтому сохранение штампует код и не стирает его, если ссылка пустая.
public class TaxSettingsRefTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private static readonly DateTime Origin = new(2020, 1, 1);

    private string Uniq() => $"{Db.NewId():N}"[..8];

    [IntegrationTest("Ссылка на налоговый код штампует строку")]
    public async Task RefStampsTaxCode()
    {
        var uniq = Uniq();
        var code = $"VAT-{uniq}";
        var taxId = await NewRecordAsync<Tax>(t =>
        {
            t.Code = $"T-{uniq}";
            t.Name = "Settings tax";
            t.Authority = Db.NewId();
            t.Jurisdiction = Db.NewId();
            t.EffectiveFrom = Origin;
        });
        var rateId = await NewRecordAsync<TaxRate>(r =>
        {
            r.Tax = taxId;
            r.Code = $"R-{uniq}";
            r.Rate = 0.2m;
            r.EffectiveFrom = Origin;
        });
        var category = await NewRecordAsync<TaxCategory>(c =>
        {
            c.Tax = taxId;
            c.Code = $"STD-{uniq}";
            c.Treatment = "STANDARD";
        });
        var taxCode = await NewRecordAsync<TaxCode>(c =>
        {
            c.Code = code;
            c.Name = "Settings code";
            c.Tax = taxId;
            c.TaxCategory = category;
            c.TaxRate = rateId;
            c.EffectiveFrom = Origin;
        });

        var settings = await SettingsAsync();
        settings.DefaultTax = taxCode;
        settings = await DictionaryManager.SaveRecordAsync(settings);
        var saved = await DictionaryManager.GetRecordAsync<TaxSettings>(settings.MetaId);
        Assert.IsTrue(saved != null, "settings row must exist");
        Assert.IsTrue(saved!.DefaultTaxCode == code,
            "stamped {0}, got {1}", code, saved.DefaultTaxCode);
    }

    [IntegrationTest("Строка кода без ссылки сохраняется")]
    public async Task StringCodeWithoutRefStillSaves()
    {
        var settings = await SettingsAsync();
        var code = $"KEEP-{Uniq()}";
        settings.DefaultTax = Guid.Empty;
        settings.DefaultTaxCode = code;
        settings = await DictionaryManager.SaveRecordAsync(settings);
        Assert.IsTrue(settings.DefaultTaxCode == code,
            "code kept {0}, got {1}", code, settings.DefaultTaxCode);
    }

    private async Task<TaxSettings> SettingsAsync()
    {
        var rows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        return rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<TaxSettings>();
    }
}
