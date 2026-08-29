using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

// Integration coverage for the Organization foundation: a legal entity is the taxable
// unit (country + currency mandatory), and a division always belongs to a legal entity.
//
// Записи создаются ТИПИЗИРОВАННЫМ IDictionaryManager<T>, а не сырыми словарями:
// тест тогда идёт тем же путём, что и бизнес-код, а опечатка в имени поля
// становится ошибкой компиляции, а не «null пришёл откуда-то» в рантайме.
public class OrganizationSetupTest : IntegrationTestScriptBase
{
    private async Task<Guid> NewCurrencyAsync(string name, string code, string symbol)
    {
        var currencies = GetService<IDictionaryManager<Currency>>();
        var currency = await currencies.NewRecordAsync();
        currency.Name = name;
        currency.Code = code;
        currency.Symbol = symbol;
        return await currencies.SaveRecordAsync(currency);
    }

    private async Task<Guid> NewCountryAsync(string name, string iso2, string iso3, string phone)
    {
        var countries = GetService<IDictionaryManager<Country>>();
        var country = await countries.NewRecordAsync();
        country.Name = name;
        country.CodeISO2 = iso2;
        country.CodeISO3 = iso3;
        country.PhoneCode = phone;
        return await countries.SaveRecordAsync(country);
    }

    [IntegrationTest("Юрлицо со страной и валютой создаётся")]
    public async Task LegalEntityIsCreated()
    {
        var currency = await NewCurrencyAsync("US Dollar", "USD", "$");
        var country = await NewCountryAsync("United States", "US", "USA", "1");

        var entities = GetService<IDictionaryManager<LegalEntity>>();
        var entity = await entities.NewRecordAsync();
        entity.Name = "ACME LLC";
        entity.RegistrationNumber = "REG-100";
        entity.Country = country;
        entity.Currency = currency;
        var le = await entities.SaveRecordAsync(entity);

        Assert.IsTrue(le != Guid.Empty, "ожидался реальный id юрлица");

        var saved = await entities.GetRecordAsync(le);
        Assert.IsTrue(saved != null, "юрлицо должно читаться из базы");
        Assert.AreEqual("ACME LLC", saved!.Name);
        Assert.IsTrue(saved.Country == country, "страна должна сохраниться как есть");
    }

    [IntegrationTest("Подразделение создаётся под юрлицом")]
    public async Task DivisionIsCreated()
    {
        var currency = await NewCurrencyAsync("Euro", "EUR", "€");
        var country = await NewCountryAsync("Germany", "DE", "DEU", "49");

        var entities = GetService<IDictionaryManager<LegalEntity>>();
        var entity = await entities.NewRecordAsync();
        entity.Name = "Muster GmbH";
        entity.RegistrationNumber = "REG-200";
        entity.Country = country;
        entity.Currency = currency;
        var le = await entities.SaveRecordAsync(entity);

        var types = GetService<IDictionaryManager<DivisionType>>();
        var type = await types.NewRecordAsync();
        type.Code = $"WH-{Db.NewId():N}"[..12];
        type.Name = "Warehouse";
        var dt = await types.SaveRecordAsync(type);

        var divisions = GetService<IDictionaryManager<Division>>();
        var division = await divisions.NewRecordAsync();
        division.Name = "Main warehouse";
        division.LegalEntity = le;
        division.DivisionType = dt;
        var div = await divisions.SaveRecordAsync(division);

        Assert.IsTrue(div != Guid.Empty, "ожидался реальный id подразделения");

        var saved = await divisions.GetRecordAsync(div);
        Assert.IsTrue(saved != null, "подразделение должно читаться из базы");
        Assert.IsTrue(saved!.LegalEntity == le,
            "подразделение должно быть привязано к своему юрлицу, а не к {0}", saved.LegalEntity);
    }

    [IntegrationTest("Юрлицо без страны и валюты отклоняется")]
    public async Task LegalEntityWithoutRefsIsRejected()
    {
        var entities = GetService<IDictionaryManager<LegalEntity>>();
        var entity = await entities.NewRecordAsync();
        entity.Name = "No refs Ltd";
        entity.RegistrationNumber = "REG-300";

        var rejected = false;
        try
        {
            rejected = await entities.SaveRecordAsync(entity) == Guid.Empty;
        }
        catch
        {
            rejected = true;
        }
        Assert.IsTrue(rejected, "юрлицо без страны/валюты должно быть отклонено обработчиком");
    }
}
