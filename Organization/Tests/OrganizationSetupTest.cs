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
    private static Task<Guid> NewCurrencyAsync(string name, string code, string symbol)
        => NewRecordAsync<Currency>(c => { c.Name = name; c.Code = code; c.Symbol = symbol; });

    private static Task<Guid> NewCountryAsync(string name, string iso2, string iso3, string phone)
        => NewRecordAsync<Country>(c =>
        {
            c.Name = name;
            c.CodeISO2 = iso2;
            c.CodeISO3 = iso3;
            c.PhoneCode = phone;
        });

    [IntegrationTest("Юрлицо со страной и валютой создаётся")]
    public async Task LegalEntityIsCreated()
    {
        var currency = await NewCurrencyAsync("US Dollar", "USD", "$");
        var country = await NewCountryAsync("United States", "US", "USA", "1");

        var le = await NewRecordAsync<LegalEntity>(e =>
        {
            e.Name = "ACME LLC";
            e.RegistrationNumber = "REG-100";
            e.Country = country;
            e.Currency = currency;
        });

        Assert.IsTrue(le != Guid.Empty, "ожидался реальный id юрлица");

        var saved = await RecordAsync<LegalEntity>(le);
        Assert.IsTrue(saved != null, "юрлицо должно читаться из базы");
        Assert.AreEqual("ACME LLC", saved!.Name);
        Assert.IsTrue(saved.Country == country, "страна должна сохраниться как есть");
    }

    [IntegrationTest("Подразделение создаётся под юрлицом")]
    public async Task DivisionIsCreated()
    {
        var currency = await NewCurrencyAsync("Euro", "EUR", "€");
        var country = await NewCountryAsync("Germany", "DE", "DEU", "49");

        var le = await NewRecordAsync<LegalEntity>(e =>
        {
            e.Name = "Muster GmbH";
            e.RegistrationNumber = "REG-200";
            e.Country = country;
            e.Currency = currency;
        });

        var dt = await NewRecordAsync<DivisionType>(t =>
        {
            t.Code = $"WH-{Db.NewId():N}"[..12];
            t.Name = "Warehouse";
        });

        var div = await NewRecordAsync<Division>(d =>
        {
            d.Name = "Main warehouse";
            d.LegalEntity = le;
            d.DivisionType = dt;
        });

        Assert.IsTrue(div != Guid.Empty, "ожидался реальный id подразделения");

        var saved = await RecordAsync<Division>(div);
        Assert.IsTrue(saved != null, "подразделение должно читаться из базы");
        Assert.IsTrue(saved!.LegalEntity == le,
            "подразделение должно быть привязано к своему юрлицу, а не к {0}", saved.LegalEntity);
    }

    [IntegrationTest("Юрлицо без страны и валюты отклоняется")]
    public async Task LegalEntityWithoutRefsIsRejected()
    {
        var rejected = false;
        try
        {
            var bad = await NewRecordAsync<LegalEntity>(e =>
            {
                e.Name = "No refs Ltd";
                e.RegistrationNumber = "REG-300";
            });
            rejected = bad == Guid.Empty;
        }
        catch
        {
            rejected = true;
        }
        Assert.IsTrue(rejected, "юрлицо без страны/валюты должно быть отклонено обработчиком");
    }
}
