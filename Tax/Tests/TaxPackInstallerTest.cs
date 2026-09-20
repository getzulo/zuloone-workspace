using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Автоустановка странового сида: ISO2 юрлица выбирает пакеты when=country.
// Имена стран в коде инсталлятора нет — только совпадение с index.json.
public class TaxPackInstallerTest : IntegrationTestScriptBase
{
    private static ITaxPackInstaller Installer => GetService<ITaxPackInstaller>();
    private static IDataPackageService Packs => GetService<IDataPackageService>();
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("Неизвестная страна не ставит ни одного пакета")]
    public async Task UnknownCountryAppliesNothing()
    {
        var n = await Installer.InstallForCountryAsync("QQ");
        Assert.IsTrue(n == 0, "для QQ пакетов when=country нет, факт {0}", n);
    }

    [IntegrationTest("SA ставит vat-SA, повтор не ломает")]
    public async Task SaudiPackAppliesIdempotently()
    {
        var listed = await Packs.ListAsync();
        Assert.IsTrue(listed.Any(p => p.Id == "LocalizationSaudiArabia/vat-SA"),
            "индекс воркспейса должен видеть LocalizationSaudiArabia/vat-SA, факт: {0}",
            string.Join(", ", listed.Select(p => p.Id)));

        var n = await Installer.InstallForCountryAsync("SA");
        Assert.IsTrue(n >= 1, "хотя бы vat-SA обязан примениться, факт {0}", n);

        var taxes = await DictionaryManager.GetRecordsAsync<Tax>("Code = 'VAT-SA'", take: 1);
        Assert.IsTrue(taxes.Count == 1, "после установки есть налог VAT-SA");

        var again = await Installer.InstallForCountryAsync("SA");
        Assert.IsTrue(again >= 1, "повторный Apply insert-only тоже Ok, факт {0}", again);
        Assert.IsTrue((await DictionaryManager.GetRecordsAsync<Tax>("Code = 'VAT-SA'", take: 2)).Count == 1,
            "повтор не плодит второй VAT-SA");
    }

    [IntegrationTest("UA ставит vat-UA")]
    public async Task UkrainePackApplies()
    {
        var listed = await Packs.ListAsync();
        Assert.IsTrue(listed.Any(p => p.Id == "LocalizationUkraine/vat-UA"),
            "индекс воркспейса должен видеть LocalizationUkraine/vat-UA, факт: {0}",
            string.Join(", ", listed.Select(p => p.Id)));

        var n = await Installer.InstallForCountryAsync("UA");
        Assert.IsTrue(n >= 1, "vat-UA обязан примениться, факт {0}", n);

        var taxes = await DictionaryManager.GetRecordsAsync<Tax>("Code = 'VAT-UA'", take: 1);
        Assert.IsTrue(taxes.Count == 1, "после установки есть налог VAT-UA");
    }

    [IntegrationTest("Сохранение юрлица со страной SA ставит пакет")]
    public async Task SavingLegalEntityInstallsPack()
    {
        var listed = await Packs.ListAsync();
        Assert.IsTrue(listed.Any(p => p.Id == "LocalizationSaudiArabia/vat-SA"),
            "пакет vat-SA должен быть в индексе");

        var country = (await DictionaryManager.GetRecordsAsync<Country>("CodeISO2 = 'SA'", take: 1))
            .FirstOrDefault();
        if (country == null)
        {
            country = DictionaryManager.NewRecord<Country>();
            country.Name = "Saudi Arabia";
            country.CodeISO2 = "SA";
            country.CodeISO3 = "SAU";
            country.PhoneCode = "966";
            country = await DictionaryManager.SaveRecordAsync(country);
        }

        var currency = (await DictionaryManager.GetRecordsAsync<Currency>("Code = 'SAR'", take: 1))
            .FirstOrDefault();
        if (currency == null)
        {
            currency = DictionaryManager.NewRecord<Currency>();
            currency.Name = "Saudi Riyal";
            currency.Code = "SAR";
            currency.Symbol = "﷼";
            currency = await DictionaryManager.SaveRecordAsync(currency);
        }

        var le = DictionaryManager.NewRecord<LegalEntity>();
        le.Name = $"Pack LE {Db.NewId():N}"[..16];
        le.RegistrationNumber = $"REG-TP-{Db.NewId():N}"[..16];
        le.Country = country.MetaId;
        le.Currency = currency.MetaId;
        await DictionaryManager.SaveRecordAsync(le);

        var taxes = await DictionaryManager.GetRecordsAsync<Tax>("Code = 'VAT-SA'", take: 1);
        Assert.IsTrue(taxes.Count == 1,
            "OnAfterSave юрлица обязан поставить VAT-SA");
    }
}
