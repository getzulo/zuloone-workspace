using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Журнал сдачи: декларация перестаёт быть внутренним отчётом без квитанции.
// Мок не ходит в сеть; исход попытки обязан лежать строкой TaxSubmission.
public class TaxSubmissionTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static ITaxReturnService Returns => GetService<ITaxReturnService>();
    private static ITaxAuthoritySubmitService Channel => GetService<ITaxAuthoritySubmitService>();

    private async Task<Guid> LegalEntityAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = "EUR";
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var le = DictionaryManager.NewRecord<LegalEntity>();
        le.Name = "ACME GmbH";
        le.RegistrationNumber = $"REG-TS-{Db.NewId():N}"[..16];
        le.Country = country.MetaId;
        le.Currency = currency.MetaId;
        return (await DictionaryManager.SaveRecordAsync(le)).MetaId;
    }

    private async Task<Guid> ReturnAsync(Guid legalEntity)
        => await Returns.BuildAsync(legalEntity, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));

    private async Task ConnectAsync(Guid legalEntity, bool reject)
    {
        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"TA-{Db.NewId():N}"[..8];
        authority.Name = "Mock authority";
        authority.CountryCode = "DE";
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var conn = DictionaryManager.NewRecord<TaxAuthorityConnection>();
        conn.Code = $"CX-{Db.NewId():N}"[..10];
        conn.Authority = authority.MetaId;
        conn.LegalEntity = legalEntity;
        conn.BaseUrl = "https://mock.tax.local";
        conn.Environment = TaxConnectionEnvironment.Sandbox;
        conn.Status = TaxConnectionStatus.Active;
        conn.IsMockReject = reject;
        await DictionaryManager.SaveRecordAsync(conn);
    }

    private Task<System.Collections.Generic.List<TaxSubmission>> RowsAsync(Guid sourceId)
        => DictionaryManager.GetRecordsAsync<TaxSubmission>($"SourceId = '{sourceId}'");

    [IntegrationTest("Без подключения мок принимает и пишет Accepted")]
    public async Task NoConnectionIsAccepted()
    {
        var id = await ReturnAsync(await LegalEntityAsync());
        var receipt = await Channel.SubmitReturnAsync(id);

        Assert.IsTrue(receipt.StartsWith("MOCK-OK:RETURN:"),
            "без подключения квитанция обязана быть принятием, факт: {0}", receipt);

        var rows = await RowsAsync(id);
        Assert.IsTrue(rows.Count == 1, "одна попытка — одна строка журнала, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].Status == "Accepted" && rows[0].Kind == "RETURN",
            "строка Accepted/RETURN, факт {0}/{1}", rows[0].Status, rows[0].Kind);
        Assert.IsTrue(rows[0].Receipt == receipt,
            "квитанция в журнале совпадает с ответом канала, факт {0}", rows[0].Receipt);
    }

    [IntegrationTest("IsMockReject пишет Rejected и не затирает прошлую попытку")]
    public async Task MockRejectIsANewRow()
    {
        var le = await LegalEntityAsync();
        await ConnectAsync(le, reject: false);
        var id = await ReturnAsync(le);

        var first = await Channel.SubmitReturnAsync(id);
        Assert.IsTrue(first.Contains("MOCK-OK"),
            "первое подключение без отказа обязано принять, факт: {0}", first);

        var rows = await DictionaryManager.GetRecordsAsync<TaxAuthorityConnection>($"LegalEntity = '{le}'", take: 1);
        var conn = rows[0];
        conn.IsMockReject = true;
        await DictionaryManager.SaveRecordAsync(conn);

        var second = await Channel.SubmitReturnAsync(id);
        Assert.IsTrue(second.Contains("MOCK-REJECT"),
            "после IsMockReject квитанция — отказ, факт: {0}", second);

        var journal = await RowsAsync(id);
        Assert.IsTrue(journal.Count == 2,
            "повтор — новая строка, прошлую не переписывают, факт {0}", journal.Count);
        Assert.IsTrue(journal.Count(r => r.Status == "Accepted") == 1
                   && journal.Count(r => r.Status == "Rejected") == 1,
            "в журнале оба исхода: Accepted и Rejected");
    }

    [IntegrationTest("Сдача декларации кладёт строку журнала")]
    public async Task FilingWritesTheJournal()
    {
        var id = await ReturnAsync(await LegalEntityAsync());
        await Db.ChangeSubtypeAsync("TaxReturn", id, "Filed");

        var rows = await RowsAsync(id);
        Assert.IsTrue(rows.Count == 1 && rows[0].Status == "Accepted",
            "переход Filed зовёт канал и пишет Accepted, факт строк {0} статус {1}",
            rows.Count, rows.Count == 0 ? "нет" : rows[0].Status);
    }
}
