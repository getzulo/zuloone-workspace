using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

// The posting profile is a ChartOfAccounts picker. Journals still resolve
// by code, so save stamps *AccountCode from the ref. A string-only row
// (tests, old stands) stays lawful. AccountingSettings is a singleton —
// cache outlives case rollback, so every case loads the existing row.
public class AccountingAccountProfileTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private string Uniq() => $"{Db.NewId():N}"[..8];

    [IntegrationTest("Ссылка на счёт штампует код в профиль")]
    public async Task RefStampsAccountCode()
    {
        var ar = await LeafAsync($"AR-{Uniq()}", "Receivable");
        var rev = await LeafAsync($"RV-{Uniq()}", "Revenue");
        var wip = await LeafAsync($"WIP-{Uniq()}", "WIP");
        var settings = await SettingsAsync();
        settings.ArAccount = ar.Id;
        settings.RevenueAccount = rev.Id;
        settings.WipAccount = wip.Id;
        settings = await DictionaryManager.SaveRecordAsync(settings);
        var saved = await DictionaryManager.GetRecordAsync<AccountingSettings>(settings.MetaId);
        Assert.IsTrue(saved != null, "профиль должен сохраниться");
        Assert.IsTrue(saved!.ArAccountCode == ar.Code,
            "дебиторка штамп {0}, факт {1}", ar.Code, saved.ArAccountCode);
        Assert.IsTrue(saved.RevenueAccountCode == rev.Code,
            "выручка штамп {0}, факт {1}", rev.Code, saved.RevenueAccountCode);
        Assert.IsTrue(saved.WipAccountCode == wip.Code,
            "незавершёнка штамп {0}, факт {1}", wip.Code, saved.WipAccountCode);
    }

    [IntegrationTest("Строковый код без ссылки по-прежнему сохраняется")]
    public async Task StringCodeWithoutRefStillSaves()
    {
        var ar = await LeafAsync($"AR-{Uniq()}", "Receivable");
        var settings = await SettingsAsync();
        settings.ArAccount = Guid.Empty;
        settings.ArAccountCode = ar.Code;
        settings = await DictionaryManager.SaveRecordAsync(settings);
        Assert.IsTrue(settings.ArAccount == Guid.Empty, "ссылка пустая");
        Assert.IsTrue(settings.ArAccountCode == ar.Code,
            "код остался {0}, факт {1}", ar.Code, settings.ArAccountCode);
    }

    [IntegrationTest("Групповой счёт в ссылке отклоняется как и код")]
    public async Task UnpostableRefIsRejected()
    {
        var group = DictionaryManager.NewRecord<ChartOfAccounts>();
        group.Code = $"G-{Uniq()}";
        group.Name = "Group";
        group.AccountType = AccountType.Asset;
        group.IsPostable = false;
        group = await DictionaryManager.SaveRecordAsync(group);

        var settings = await SettingsAsync();
        settings.ArAccount = group.MetaId;
        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(settings); }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("не проводимый") || reason.Contains("группа"),
            "группа в профиле обязана быть отклонена, факт: {0}", reason);
    }

    private async Task<AccountingSettings> SettingsAsync()
    {
        var rows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        return rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<AccountingSettings>();
    }

    private async Task<(Guid Id, string Code)> LeafAsync(string code, string name)
    {
        var row = DictionaryManager.NewRecord<ChartOfAccounts>();
        row.Code = code;
        row.Name = name;
        row.AccountType = AccountType.Asset;
        row.IsPostable = true;
        row = await DictionaryManager.SaveRecordAsync(row);
        return (row.MetaId, row.Code);
    }
}
