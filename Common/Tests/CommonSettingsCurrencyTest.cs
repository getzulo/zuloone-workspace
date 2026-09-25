using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Валюта по умолчанию — ссылка на Currency. Код штампуется для статуса тенанта.
// Пустая ссылка код не стирает. Загрузка подставляет ссылку по уже записанному коду.
// CommonSettings — синглтон, кэш переживает откат кейса: берём существующую строку.
public class CommonSettingsCurrencyTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private string Code3() => $"{Db.NewId():N}"[..3].ToUpperInvariant();

    [IntegrationTest("Ссылка на валюту штампует код")]
    public async Task RefStampsCurrencyCode()
    {
        var code = Code3();
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Code = code;
        currency.Name = "Settings currency";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var settings = await SettingsAsync();
        settings.DefaultCurrency = currency.MetaId;
        settings = await DictionaryManager.SaveRecordAsync(settings);
        var saved = await DictionaryManager.GetRecordAsync<CommonSettings>(settings.MetaId);
        Assert.IsTrue(saved != null, "settings row must exist");
        Assert.IsTrue(saved!.DefaultCurrencyCode == code,
            "stamped {0}, got {1}", code, saved.DefaultCurrencyCode);
        Assert.IsTrue(saved.DefaultCurrency == currency.MetaId, "currency ref must stay");
    }

    [IntegrationTest("Код без ссылки сохраняется и находится при загрузке")]
    public async Task CodeWithoutRefResolvesOnLoad()
    {
        var code = Code3();
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Code = code;
        currency.Name = "Code only currency";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var settings = await SettingsAsync();
        settings.DefaultCurrency = Guid.Empty;
        settings.DefaultCurrencyCode = code;
        settings = await DictionaryManager.SaveRecordAsync(settings);
        Assert.IsTrue(settings.DefaultCurrencyCode == code,
            "code kept {0}, got {1}", code, settings.DefaultCurrencyCode);

        var loaded = await DictionaryManager.GetRecordAsync<CommonSettings>(settings.MetaId);
        Assert.IsTrue(loaded != null, "settings row must load");
        Assert.IsTrue(loaded!.DefaultCurrency == currency.MetaId,
            "load must resolve the code to the currency");
        Assert.IsTrue(loaded.DefaultCurrencyCode == code, "code stays after load");
    }

    [IntegrationTest("Новая запись берёт валюту и дату начала из настроек")]
    public async Task NewRecordTakesCurrencyAndStartDate()
    {
        var code = Code3();
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Code = code;
        currency.Name = "Create default";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var settings = await SettingsAsync();
        var priorRef = settings.DefaultCurrency;
        var priorCode = settings.DefaultCurrencyCode;
        try
        {
            settings.DefaultCurrency = currency.MetaId;
            settings = await DictionaryManager.SaveRecordAsync(settings);

            var preview = await GetService<IRecordDefaults>().ForNewAsync("ExchangeRate");
            Assert.IsTrue(preview.TryGetValue("Currency", out var cur) && cur is Guid curId && curId == currency.MetaId,
                "курс открывается в валюте настроек");
            Assert.IsTrue(preview.TryGetValue("EffectiveFrom", out var from) && from is DateTime day && day.Date == DateTime.UtcNow.Date,
                "дата начала нового курса — сегодня");

            var entry = await GetService<IRecordDefaults>().ForNewAsync("JournalEntry");
            Assert.IsTrue(entry.TryGetValue("Currency", out var entryCur) && entryCur is Guid entryId && entryId == currency.MetaId,
                "проводка открывается в валюте настроек");
        }
        finally
        {
            settings.DefaultCurrency = priorRef;
            settings.DefaultCurrencyCode = priorCode;
            await DictionaryManager.SaveRecordAsync(settings);
        }
    }

    private async Task<CommonSettings> SettingsAsync()
    {
        var rows = await DictionaryManager.GetRecordsAsync<CommonSettings>(null, 1);
        return rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<CommonSettings>();
    }
}
