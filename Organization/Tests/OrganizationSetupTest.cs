using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (Currency, Country, LegalEntity, Division…). Тестовым скриптам
// этот namespace НЕ приходит глобальным using'ом — без него `Currency` связывается
// с посторонним недоступным типом, и ошибка (CS0122) описывает не ту причину.
using ZuloOne.Runtime.Generated;

// Integration coverage for the Organization foundation: a legal entity is the taxable
// unit (country + currency mandatory), and a division always belongs to a legal entity.
//
// Записи создаются ТИПИЗИРОВАННО через IDictionaryManager — NewRecord<T> → заполнить
// свойства → SaveRecordAsync, ровно как это делает бизнес-код. Опечатка в имени поля
// становится ошибкой компиляции, а не «null пришёл откуда-то» в рантайме, и сохранённая
// сущность остаётся на руках: читать её обратно приходится только там, где проверяется
// именно ЧТЕНИЕ.
public class OrganizationSetupTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private static async Task<Currency> NewCurrencyAsync(string name, string code, string symbol)
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = name;
        currency.Code = code;
        currency.Symbol = symbol;
        return await DictionaryManager.SaveRecordAsync(currency);
    }

    private static async Task<Country> NewCountryAsync(string name, string iso2, string iso3, string phone)
    {
        var country = DictionaryManager.NewRecord<Country>();
        country.Name = name;
        country.CodeISO2 = iso2;
        country.CodeISO3 = iso3;
        country.PhoneCode = phone;
        return await DictionaryManager.SaveRecordAsync(country);
    }

    [IntegrationTest("Юрлицо со страной и валютой создаётся")]
    public async Task LegalEntityIsCreated()
    {
        var currency = await NewCurrencyAsync("US Dollar", "USD", "$");
        var country = await NewCountryAsync("United States", "US", "USA", "1");

        var entity = DictionaryManager.NewRecord<LegalEntity>();
        entity.Name = "ACME LLC";
        entity.RegistrationNumber = "REG-100";
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        entity = await DictionaryManager.SaveRecordAsync(entity);

        Assert.IsTrue(entity.MetaId != Guid.Empty, "ожидался реальный id юрлица");

        var saved = await DictionaryManager.GetRecordAsync<LegalEntity>(entity.MetaId);
        Assert.IsTrue(saved != null, "юрлицо должно читаться из базы");
        Assert.AreEqual("ACME LLC", saved!.Name);
        Assert.IsTrue(saved.Country == country.MetaId, "страна должна сохраниться как есть");
    }

    [IntegrationTest("Подразделение создаётся под юрлицом")]
    public async Task DivisionIsCreated()
    {
        var currency = await NewCurrencyAsync("Euro", "EUR", "€");
        var country = await NewCountryAsync("Germany", "DE", "DEU", "49");

        var entity = DictionaryManager.NewRecord<LegalEntity>();
        entity.Name = "Muster GmbH";
        entity.RegistrationNumber = "REG-200";
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        entity = await DictionaryManager.SaveRecordAsync(entity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"WH-{Guid.NewGuid():N}"[..12];
        divisionType.Name = "Warehouse";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Main warehouse";
        division.LegalEntity = entity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        Assert.IsTrue(division.MetaId != Guid.Empty, "ожидался реальный id подразделения");

        var saved = await DictionaryManager.GetRecordAsync<Division>(division.MetaId);
        Assert.IsTrue(saved != null, "подразделение должно читаться из базы");
        Assert.IsTrue(saved!.LegalEntity == entity.MetaId,
            "подразделение должно быть привязано к своему юрлицу, а не к {0}", saved.LegalEntity);
    }

    [IntegrationTest("Юрлицо без страны и валюты отклоняется")]
    public async Task LegalEntityWithoutRefsIsRejected()
    {
        var rejected = false;
        try
        {
            var bad = DictionaryManager.NewRecord<LegalEntity>();
            bad.Name = "No refs Ltd";
            bad.RegistrationNumber = "REG-300";
            bad = await DictionaryManager.SaveRecordAsync(bad);
            rejected = bad.MetaId == Guid.Empty;
        }
        catch
        {
            // Обработчик отказывает БРОСКОМ, и бросок портит объемлющую транзакцию
            // прогона — поэтому после catch базу больше не трогаем, а утверждаем
            // на самом отказе.
            rejected = true;
        }
        Assert.IsTrue(rejected, "юрлицо без страны/валюты должно быть отклонено обработчиком");
    }
}
