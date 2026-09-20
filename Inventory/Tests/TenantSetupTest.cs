using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class TenantSetupTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("Без залитой страны ApplyOrg отказывает")]
    public async Task ApplyOrg_without_country_fails()
    {
        var setup = GetService<ITenantSetup>();
        var failed = false;
        try
        {
            await setup.ApplyOrgAsync("No country LE", $"REG-NO-{Db.NewId():N}"[..16], "QQ", "QQQ");
        }
        catch (Exception)
        {
            failed = true;
        }
        Assert.IsTrue(failed, "ApplyOrg без страны и валюты должен отказать");
    }

    [IntegrationTest("ApplyOrg после справочников: одно юрлицо, одно подразделение, один склад; повтор не плодит склад")]
    public async Task ApplyOrg_is_idempotent_on_registration_number()
    {
        var country = DictionaryManager.NewRecord<Country>();
        country.Name = $"SeedCtry-{Db.NewId():N}"[..16];
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country = await DictionaryManager.SaveRecordAsync(country);

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = $"SeedCur-{Db.NewId():N}"[..12];
        currency.Code = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "¤";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var setup = GetService<ITenantSetup>();
        var packs = GetService<IDataPackageService>();
        var listed = await packs.ListAsync();
        // Индекс читает диск воркспейса. На тенанте без overlay пакеты
        // отсутствуют — ApplyOrg от этого не зависит: страна и валюта
        // уже залиты выше. Пустой список не валит идемпотентность.
        if (listed.Count > 0)
        {
            var ids = string.Join(", ", listed.Select(p => p.Id));
            Assert.IsTrue(listed.Any(p => p.Id == "Common/catalogs"),
                "индекс должен видеть Common/catalogs, факт: {0}", ids);
            Assert.IsTrue(listed.Any(p => p.Id == "Common/cities-SA"),
                "индекс должен видеть Common/cities-SA, факт: {0}", ids);
        }

        var reg = $"REG-TS-{Db.NewId():N}"[..16];
        var first = await setup.ApplyOrgAsync("Seed LE", reg, country.CodeISO2!, currency.Code!);
        Assert.IsTrue(Equals(first["ok"], true), "первый ApplyOrg должен пройти");
        Assert.IsTrue(Equals(first["createdLegalEntity"], true), "первое юрлицо создаётся");
        Assert.IsTrue(Equals(first["createdStore"], true), "первый склад создаётся");
        Assert.IsTrue(Equals(first["tradeProfile"], "BuySell"),
            "новый тенант — купил-продал, факт {0}", first["tradeProfile"]);
        Assert.IsTrue(await GetService<ITradeProfileService>().IsBuySellAsync(),
            "ApplyOrg штампует BuySell");

        var les = await DictionaryManager.GetRecordsAsync<LegalEntity>($"RegistrationNumber = '{reg}'");
        Assert.AreEqual(1, les.Count, "ровно одно юрлицо с этим регномером");
        var leId = les[0].MetaId;
        var divs = await DictionaryManager.GetRecordsAsync<Division>($"LegalEntity = '{leId}'");
        Assert.AreEqual(1, divs.Count, "одно подразделение");
        var stores = 0;
        foreach (var d in divs)
            stores += (await DictionaryManager.GetRecordsAsync<Store>($"Division = '{d.MetaId}'")).Count;
        Assert.AreEqual(1, stores, "один склад");

        var second = await setup.ApplyOrgAsync("Seed LE again", reg, country.CodeISO2!, currency.Code!);
        Assert.IsTrue(Equals(second["createdLegalEntity"], false), "повтор не плодит юрлицо");
        Assert.IsTrue(Equals(second["createdStore"], false), "повтор не плодит склад");
        Assert.AreEqual(1, (await DictionaryManager.GetRecordsAsync<LegalEntity>($"RegistrationNumber = '{reg}'")).Count);
        var stores2 = 0;
        foreach (var d in await DictionaryManager.GetRecordsAsync<Division>($"LegalEntity = '{leId}'"))
            stores2 += (await DictionaryManager.GetRecordsAsync<Store>($"Division = '{d.MetaId}'")).Count;
        Assert.AreEqual(1, stores2, "второй склад не появился");
    }

    [IntegrationTest("ApplyOrg для SA ставит страновой налоговый пакет")]
    public async Task ApplyOrg_installs_saudi_tax_pack()
    {
        var packs = GetService<IDataPackageService>();
        var listed = await packs.ListAsync();
        Assert.IsTrue(listed.Any(p => p.Id == "LocalizationSaudiArabia/vat-SA"),
            "индекс должен видеть vat-SA, факт: {0}",
            string.Join(", ", listed.Select(p => p.Id)));

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

        var setup = GetService<ITenantSetup>();
        var reg = $"REG-TX-{Db.NewId():N}"[..16];
        var result = await setup.ApplyOrgAsync("SA tax LE", reg, "SA", currency.Code!);
        Assert.IsTrue(Equals(result["ok"], true), "ApplyOrg SA должен пройти");

        var taxes = await DictionaryManager.GetRecordsAsync<Tax>("Code = 'VAT-SA'", take: 1);
        Assert.IsTrue(taxes.Count == 1, "ApplyOrg SA ставит налог VAT-SA");
    }
}
