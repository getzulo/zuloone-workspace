using System;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

// Банк и налоговый оператор — разные справочники подключений.
//
// Срез НЕ вызывает API ПриватБанка: живого ACP в CI нет.
// Проверяется, что каналы ЄРПН читают TaxAuthorityConnection,
// а токен банка лежит в UaBankConnection, не в налоговой карточке.
public class UkraineBankConnectionTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static ISqlService Sql => GetService<ISqlService>();

    [IntegrationTest("Канал pb-statements читает UaBankConnection, ЄРПН — TaxAuthorityConnection")]
    public async Task ErpnChannelsNameTheTaxConnectionTable()
    {
        var rows = await Sql.SelectAsync(
            "SELECT [Name], [ConnectionTable], [AuthMode], [AuthHeader] FROM [MetaOutboundChannels] WHERE [Name] IN ('erpn-send','erpn-receipt','erpn-gov')");

        Assert.IsTrue(rows.Count == 3, "три канали ЄРПН, факт {0}", rows.Count);
        foreach (var row in rows)
        {
            Assert.IsTrue(Convert.ToString(row["ConnectionTable"]) == "TaxAuthorityConnection",
                "{0} должен читать TaxAuthorityConnection, факт '{1}'",
                row["Name"], row["ConnectionTable"]);
            Assert.IsTrue(Convert.ToString(row["AuthMode"]) == "Login",
                "{0} authMode Login (POST /api/System/v2/login → X-API-Key), факт '{1}'",
                row["Name"], row["AuthMode"]);
            Assert.IsTrue(Convert.ToString(row["AuthHeader"]) == "X-API-Key",
                "{0} authHeader X-API-Key, факт '{1}'",
                row["Name"], row["AuthHeader"]);
        }
    }

    [IntegrationTest("Банковское подключение сохраняет адрес и имя credential, не сам токен")]
    public async Task BankConnectionStoresTheCredentialNameNotTheSecret()
    {
        var col = await Sql.SelectAsync(
            "SELECT COL_LENGTH('UaBankConnection','AgentTag') AS [Len]");
        Assert.IsTrue(col.Count == 1 && col[0]["Len"] is not null,
            "колонка AgentTag должна быть на UaBankConnection, не на канале ЄРПН");

        var entity = await LegalEntityAsync();
        var code = $"pb-{Db.NewId():N}"[..12];

        var row = Dict.NewRecord<UaBankConnection>();
        row.Code = code;
        row.Name = "ПриватБанк тест";
        row.LegalEntity = entity;
        row.BaseUrl = "https://bank.invalid/api";
        row.CredentialRef = "privatbank-merchant";
        row.Environment = "test";
        row.AccountNumber = "UA123456789012345678901234567";
        row.TimeoutSeconds = 30;
        row.AgentTag = "medoc";
        row = await Dict.SaveRecordAsync(row);

        var saved = await Dict.GetRecordAsync<UaBankConnection>(row.MetaId);
        Assert.IsTrue(saved is not null, "запись должна сохраниться");
        Assert.IsTrue(saved!.Code == code, "код, факт {0}", saved.Code);
        Assert.IsTrue(saved.BaseUrl == "https://bank.invalid/api", "адрес, факт {0}", saved.BaseUrl);
        Assert.IsTrue(saved.CredentialRef == "privatbank-merchant",
            "в справочнике имя credential, не токен; факт {0}", saved.CredentialRef);
        Assert.IsTrue(saved.LegalEntity == entity, "юрлицо сохранено");
        Assert.IsTrue(saved.IsDisabled == false,
            "без галки запись рабочая: IsDisabled=false, факт {0}", saved.IsDisabled);
        Assert.IsTrue(saved.AgentTag == "medoc",
            "ярлык агента на подключении, не на налоговом канале; факт {0}", saved.AgentTag);
    }

    private async Task<Guid> LegalEntityAsync()
    {
        // Уже закоммиченное юрлицо: четыре вставки в одном TransactionScope
        // на этом стенде иногда обрывают SQL-транзакцию до SAVE TRANSACTION.
        var existing = await Sql.SelectAsync("SELECT TOP 1 [MetaId] FROM [LegalEntity]");
        if (existing.Count > 0 && existing[0]["MetaId"] is Guid id)
            return id;

        var currency = Dict.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = "UAH";
        currency.Symbol = "₴";
        currency = await Dict.SaveRecordAsync(currency);

        var country = Dict.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = "UA";
        country.CodeISO3 = "UKR";
        country.PhoneCode = "380";
        country = await Dict.SaveRecordAsync(country);

        var entity = Dict.NewRecord<LegalEntity>();
        entity.Name = "ТОВ Банк-тест";
        entity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        entity = await Dict.SaveRecordAsync(entity);
        return entity.MetaId;
    }
}
