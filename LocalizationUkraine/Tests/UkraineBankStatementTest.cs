using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Розбір виписки Autoclient. Живий acp.privatbank.ua не викликається:
// тіло — з офіційного прикладу transactions (REF, REFN, TRANTYPE, SUM, OSND).
public class UkraineBankStatementTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static ISqlService Sql => GetService<ISqlService>();
    private static IInformationRegisterService Info => GetService<IInformationRegisterService>();
    private static IUaBank Bank => GetService<IUaBank>();

    [IntegrationTest("Офіційний JSON Autoclient пише рядок UaBankStatement з ключем REF+REFN")]
    public async Task OfficialTransactionsJsonIsStoredOnce()
    {
        var entity = await LegalEntityAsync();
        var code = $"pb-{Db.NewId():N}"[..12];
        var row = Dict.NewRecord<UaBankConnection>();
        row.Code = code;
        row.Name = "ПриватБанк виписка";
        row.LegalEntity = entity;
        row.BaseUrl = "https://acp.privatbank.ua/api";
        row.CredentialRef = "privatbank-merchant";
        row.Environment = "test";
        row.AccountNumber = "UA943052990000026100050001037";
        row = await Dict.SaveRecordAsync(row);

        const string body = """
            {
              "status": "SUCCESS",
              "exist_next_page": false,
              "transactions": [
                {
                  "AUT_CNTR_CRF": "14360570",
                  "AUT_CNTR_NAM": "ПРОЦ ВИТР ЗА СТРОК КОШТ СУБ(UAH)",
                  "OSND": "Нарахування відсотків згідно з депозитним договором",
                  "SUM": "0.01",
                  "REF": "DNCHK0108B1WKX",
                  "REFN": "1",
                  "DAT_OD": "07.01.2020",
                  "DATE_TIME_DAT_OD_TIM_P": "07.01.2020 02:58:00",
                  "TRANTYPE": "C",
                  "FL_REAL": "r",
                  "PR_PR": "r"
                },
                {
                  "REF": "SKIPREV",
                  "REFN": "1",
                  "SUM": "10.00",
                  "TRANTYPE": "D",
                  "FL_REAL": "r",
                  "PR_PR": "t",
                  "OSND": "сторно"
                }
              ]
            }
            """;

        var first = await Bank.ImportBodyAsync(row.MetaId, body);
        Assert.IsTrue(first == 1, "проведена C 0.01 імпортується, сторно PR_PR=t ні; факт {0}", first);

        var slice = await Info.SliceLastAsync(
            "UaBankStatement",
            DateTime.UtcNow,
            new Dictionary<string, object?>
            {
                ["LegalEntity"] = entity,
                ["BankRef"] = "DNCHK0108B1WKX1",
            });
        Assert.IsTrue(slice.Count == 1, "ключ REF+REFN, факт {0}", slice.Count);
        Assert.IsTrue(Convert.ToString(slice[0]["TranType"]) == "C",
            "TRANTYPE C, факт {0}", slice[0]["TranType"]);
        Assert.IsTrue(Convert.ToDecimal(slice[0]["Amount"]) == 0.01m,
            "SUM 0.01, факт {0}", slice[0]["Amount"]);

        var second = await Bank.ImportBodyAsync(row.MetaId, body);
        Assert.IsTrue(second == 0, "повтор REF+REFN не дублює рядок, факт {0}", second);
    }

    [IntegrationTest("Канал pb-statements читає UaBankConnection і кладе токен у заголовок token")]
    public async Task StatementsChannelNamesTheBankConnectionTable()
    {
        var rows = await Sql.SelectAsync(
            "SELECT [Name], [ConnectionTable], [AuthMode], [AuthHeader], [Method] FROM [MetaOutboundChannels] WHERE [Name] = 'pb-statements'");
        Assert.IsTrue(rows.Count == 1, "канал pb-statements, факт {0}", rows.Count);
        Assert.IsTrue(Convert.ToString(rows[0]["ConnectionTable"]) == "UaBankConnection",
            "connectionTable UaBankConnection, факт '{0}'", rows[0]["ConnectionTable"]);
        Assert.IsTrue(Convert.ToString(rows[0]["AuthMode"]) == "Header",
            "authMode Header, факт '{0}'", rows[0]["AuthMode"]);
        Assert.IsTrue(Convert.ToString(rows[0]["AuthHeader"]) == "token",
            "офіційний заголовок token, не id, факт '{0}'", rows[0]["AuthHeader"]);
        Assert.IsTrue(Convert.ToString(rows[0]["Method"]) == "GET",
            "GET /statements/transactions, факт '{0}'", rows[0]["Method"]);
    }

    private async Task<Guid> LegalEntityAsync()
    {
        // The manager applies the provider's row limit; TOP is SQL Server-only.
        var existing = await Dict.GetRecordsAsync<LegalEntity>(take: 1);
        if (existing.Count > 0)
            return existing[0].MetaId;

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
        entity.Name = "ТОВ Банк-виписка";
        entity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        entity = await Dict.SaveRecordAsync(entity);
        return entity.MetaId;
    }
}
