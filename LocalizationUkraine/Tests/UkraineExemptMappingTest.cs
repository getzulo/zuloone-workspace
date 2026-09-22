using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// ═══ СОПОСТАВЛЕНИЕ ОСВОБОЖДЕНИЯ СО СМЕНОЙ РЕЖИМА ═════════════════════════════
//
// Коды берём из поставки vat-UA (UA-E / UA-S), а не собираем свой контур:
// создание TaxRate на отравленном стенде падает в обработчике
// («ITaxService is not available»), хотя чтение ставки из теста проходит.
public class UkraineExemptMappingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IUaFirstEvent FirstEvent => GetService<IUaFirstEvent>();

    private sealed class Setup
    {
        public Guid LegalEntity;
        public Guid DefaultCode;
        public Guid ExemptCode;
        public Guid Country;
        public Guid Currency;
    }

    private async Task<Setup> SetupAsync()
    {
        await Db.SetAccountingPeriodsAsync(null, null);

        var exempt = (await DictionaryManager.GetRecordsAsync<TaxCode>("Code = 'UA-E'", take: 1))
            .FirstOrDefault();
        var standard = (await DictionaryManager.GetRecordsAsync<TaxCode>("Code = 'UA-S'", take: 1))
            .FirstOrDefault();
        Assert.IsTrue(exempt is not null && standard is not null,
            "поставка vat-UA обязана дать UA-E и UA-S");

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = $"U{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "₴";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "380";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "FOP Mapping";
        legalEntity.RegistrationNumber = $"REG-MAP-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var uaRows = await DictionaryManager.GetRecordsAsync<LocalizationUkraineSettings>(null, 1);
        var uaSettings = uaRows.Count > 0 ? uaRows[0] : DictionaryManager.NewRecord<LocalizationUkraineSettings>();
        uaSettings.ExemptVatCode = exempt!.Code;
        await DictionaryManager.SaveRecordAsync(uaSettings);

        return new Setup
        {
            LegalEntity = legalEntity.MetaId,
            DefaultCode = standard!.MetaId,
            ExemptCode = exempt.MetaId,
            Country = country.MetaId,
            Currency = currency.MetaId,
        };
    }

    private async Task SetRegimeAsync(Setup s, UaTaxRegime regime)
    {
        await Db.UpdateAsync("LegalEntity", s.LegalEntity,
            new Dictionary<string, object?>
            {
                ["UaTaxRegime"] = (int)regime,
                ["Country"] = s.Country,
                ["Currency"] = s.Currency,
            });
        await FirstEvent.SyncExemptMappingAsync(s.LegalEntity);
    }

    private async Task<List<TaxMapping>> MappingsOfAsync(Guid legalEntity)
        => (await DictionaryManager.GetRecordsAsync<TaxMapping>(
                $"SourceType = 'LegalEntity' AND SourceId = '{legalEntity}'"))
            .ToList();

    [IntegrationTest("Смена режима заводит, снимает и не затирает сопоставление освобождения; пустой код ничего не заводит")]
    public async Task RegimeChangeMaintainsExemptMapping()
    {
        var s = await SetupAsync();

        await SetRegimeAsync(s, UaTaxRegime.SimplifiedNoVat);
        var live = (await MappingsOfAsync(s.LegalEntity)).Where(m => !m.IsDisabled).ToList();
        Assert.IsTrue(live.Count == 1, "спрощенець: ожидалась 1 живая строка, факт {0}", live.Count);
        Assert.IsTrue(live[0].TaxCode == s.ExemptCode,
            "сопоставление обязано указать код освобождения");

        await SetRegimeAsync(s, UaTaxRegime.VatPayer);
        live = (await MappingsOfAsync(s.LegalEntity)).Where(m => !m.IsDisabled).ToList();
        Assert.IsTrue(live.Count == 0,
            "у плательщика ПДВ живого сопоставления быть не должно, факт {0}", live.Count);

        var foreign = DictionaryManager.NewRecord<TaxMapping>();
        foreign.SourceType = "LegalEntity";
        foreign.SourceId = s.LegalEntity;
        foreign.TaxCode = s.DefaultCode;
        foreign.Priority = 0;
        foreign.EffectiveFrom = new DateTime(2020, 1, 1);
        await DictionaryManager.SaveRecordAsync(foreign);

        await SetRegimeAsync(s, UaTaxRegime.SimplifiedNoVat);
        live = (await MappingsOfAsync(s.LegalEntity)).Where(m => !m.IsDisabled).ToList();
        Assert.IsTrue(live.Count == 1, "чужую строку нельзя дублировать, факт {0}", live.Count);
        Assert.IsTrue(live[0].TaxCode == s.DefaultCode,
            "чужой код обязан остаться, факт {0}", live[0].TaxCode);

        var uaRows = await DictionaryManager.GetRecordsAsync<LocalizationUkraineSettings>(null, 1);
        uaRows[0].ExemptVatCode = "";
        await DictionaryManager.SaveRecordAsync(uaRows[0]);

        var other = DictionaryManager.NewRecord<LegalEntity>();
        other.Name = "FOP Empty";
        other.RegistrationNumber = $"REG-EMP-{Db.NewId():N}"[..16];
        other.Country = s.Country;
        other.Currency = s.Currency;
        other = await DictionaryManager.SaveRecordAsync(other);
        await Db.UpdateAsync("LegalEntity", other.MetaId,
            new Dictionary<string, object?>
            {
                ["UaTaxRegime"] = (int)UaTaxRegime.SimplifiedNoVat,
                ["Country"] = s.Country,
                ["Currency"] = s.Currency,
            });
        await FirstEvent.SyncExemptMappingAsync(other.MetaId);

        live = (await MappingsOfAsync(other.MetaId)).Where(m => !m.IsDisabled).ToList();
        Assert.IsTrue(live.Count == 0,
            "пусто в настройках — строка не заводится, факт {0}", live.Count);
    }
}
