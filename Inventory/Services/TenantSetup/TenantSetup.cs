using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;

// Каркас тенанта: пакеты справочников заливает IDataPackageService,
// здесь только то, что пакет не закроет — имя юрлица и склад.
public partial class TenantSetup
{
    private static IDictionaryManager Live => ScriptServices.Get<IDictionaryManager>();
    private static IDataPackageService Packs => ScriptServices.Get<IDataPackageService>();

    public async Task<Dictionary<string, object?>> StatusAsync()
    {
        var packs = await Packs.ListAsync();
        var hasLe = (await Live.GetRecordsAsync<LegalEntity>("1 = 1", take: 1)).Count > 0;
        var settings = (await Live.GetRecordsAsync<CommonSettings>("1 = 1", take: 1)).FirstOrDefault();
        string? iso = null;
        if (settings?.DefaultCountryCode is Guid countryId && countryId != Guid.Empty)
        {
            var c = await Live.GetRecordAsync<Country>(countryId);
            iso = c?.CodeISO2;
        }

        var packList = new List<Dictionary<string, object?>>();
        foreach (var p in packs)
        {
            packList.Add(new Dictionary<string, object?>
            {
                ["id"] = p.Id,
                ["caption"] = p.Caption,
                ["captionRu"] = p.CaptionRu,
                ["when"] = p.When,
                ["country"] = p.Country,
                ["sort"] = p.Sort,
                ["applied"] = p.Applied,
            });
        }

        return new Dictionary<string, object?>
        {
            ["hasLegalEntity"] = hasLe,
            ["defaultCountryIso2"] = iso,
            ["defaultCurrencyCode"] = settings?.DefaultCurrencyCode,
            ["packages"] = packList,
        };
    }

    public async Task<Dictionary<string, object?>> SetDefaultsAsync(string countryIso2, string currencyCode)
    {
        var country = await RequireCountryAsync(countryIso2);
        var currency = await RequireCurrencyAsync(currencyCode);
        await WriteDefaultsAsync(country, currency);
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["countryIso2"] = country.CodeISO2,
            ["currencyCode"] = currency.Code,
        };
    }

    public async Task<Dictionary<string, object?>> ApplyOrgAsync(
        string legalEntityName, string registrationNumber, string countryIso2, string currencyCode)
    {
        var name = (legalEntityName ?? "").Trim();
        var reg = (registrationNumber ?? "").Trim();
        if (name.Length == 0) throw new InvalidOperationException("Legal entity name is required.");
        if (reg.Length == 0) throw new InvalidOperationException("Registration number is required.");

        var country = await RequireCountryAsync(countryIso2);
        var currency = await RequireCurrencyAsync(currencyCode);

        var existing = (await Live.GetRecordsAsync<LegalEntity>($"RegistrationNumber = '{Lit(reg)}'", take: 1))
            .FirstOrDefault();
        var createdLe = existing == null;
        LegalEntity le;
        if (existing != null)
        {
            le = existing;
        }
        else
        {
            le = Live.NewRecord<LegalEntity>();
            le.Name = name.Length <= 160 ? name : name[..160];
            le.RegistrationNumber = reg.Length <= 32 ? reg : reg[..32];
            le.Country = country.MetaId;
            le.Currency = currency.MetaId;
            le = await Live.SaveRecordAsync(le);
        }

        var type = (await Live.GetRecordsAsync<DivisionType>("Code = 'MAIN'", take: 1)).FirstOrDefault();
        if (type == null)
        {
            type = Live.NewRecord<DivisionType>();
            type.Code = "MAIN";
            type.Name = "Headquarters";
            type = await Live.SaveRecordAsync(type);
        }

        var division = (await Live.GetRecordsAsync<Division>($"LegalEntity = '{le.MetaId}'", take: 1)).FirstOrDefault();
        if (division == null)
        {
            division = Live.NewRecord<Division>();
            division.Name = le.Name;
            division.LegalEntity = le.MetaId;
            division.DivisionType = type.MetaId;
            division = await Live.SaveRecordAsync(division);
        }

        var createdStore = false;
        var storeCount = 0;
        foreach (var d in await Live.GetRecordsAsync<Division>($"LegalEntity = '{le.MetaId}'"))
            storeCount += (await Live.GetRecordsAsync<Store>($"Division = '{d.MetaId}'")).Count;

        if (storeCount == 0)
        {
            var store = Live.NewRecord<Store>();
            store.Name = $"{le.Name} store";
            if (store.Name.Length > 128) store.Name = store.Name[..128];
            store.Division = division.MetaId;
            store.IsSimple = true;
            store.IsSalesPoint = false;
            store = await Live.SaveRecordAsync(store);

            var zone = Live.NewRecord<StoreZone>();
            zone.Name = "Main";
            zone.Store = store.MetaId;
            zone.IsBarcodeTracking = false;
            await Live.SaveRecordAsync(zone);
            createdStore = true;
        }

        await WriteDefaultsAsync(country, currency);

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["legalEntityId"] = le.MetaId.ToString(),
            ["createdLegalEntity"] = createdLe,
            ["createdStore"] = createdStore,
        };
    }

    private static async Task<Country> RequireCountryAsync(string countryIso2)
    {
        var iso = (countryIso2 ?? "").Trim().ToUpperInvariant();
        if (iso.Length != 2)
            throw new InvalidOperationException("Country ISO2 is required.");
        var country = (await Live.GetRecordsAsync<Country>($"CodeISO2 = '{Lit(iso)}'", take: 1)).FirstOrDefault();
        if (country == null)
            throw new InvalidOperationException($"Country '{iso}' is not in the catalog. Apply the catalogs package first.");
        return country;
    }

    private static async Task<Currency> RequireCurrencyAsync(string currencyCode)
    {
        var code = (currencyCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0)
            throw new InvalidOperationException("Currency code is required.");
        var currency = (await Live.GetRecordsAsync<Currency>($"Code = '{Lit(code)}'", take: 1)).FirstOrDefault();
        if (currency == null)
            throw new InvalidOperationException($"Currency '{code}' is not in the catalog. Apply the catalogs package first.");
        return currency;
    }

    private static async Task WriteDefaultsAsync(Country country, Currency currency)
    {
        var settings = (await Live.GetRecordsAsync<CommonSettings>("1 = 1", take: 1)).FirstOrDefault()
            ?? Live.NewRecord<CommonSettings>();
        settings.DefaultCountryCode = country.MetaId;
        settings.DefaultCurrencyCode = currency.Code;
        await Live.SaveRecordAsync(settings);
    }

    private static string Lit(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
