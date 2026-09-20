using System;
using System.Threading.Tasks;
using ZuloOne.Runtime;

// Страновые пакеты (when=country в DataPackages/index.json) ставятся по ISO2
// юрлица, а не по имени страны в коде. Сид КСА и Украины сосуществуют —
// повторный Apply в режиме insert не затирает правки тенанта.
//
// Имя историческое (ITaxPackInstaller в комментарии Organization): контракт
// when=country шире налога, поэтому сюда же попадает Common/cities-SA.
public partial class TaxPackInstaller
{
    private readonly IDataPackageService _packs;

    public TaxPackInstaller(IDataPackageService packs) => _packs = packs;

    /// <summary>Apply every <c>when=country</c> pack whose ISO2 matches.
    /// Returns how many packs applied without error. Unknown country → 0.</summary>
    public async Task<int> InstallForCountryAsync(string countryIso2)
    {
        var iso = (countryIso2 ?? "").Trim().ToUpperInvariant();
        if (iso.Length != 2) return 0;

        var applied = 0;
        foreach (var pack in await _packs.ListAsync())
        {
            if (!string.Equals(pack.When, "country", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(pack.Country, iso, StringComparison.OrdinalIgnoreCase))
                continue;
            var result = await _packs.ApplyAsync(pack.Id);
            if (result.Ok) applied++;
        }
        return applied;
    }
}
