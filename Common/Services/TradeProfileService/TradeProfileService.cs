#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;

// One door for the tenant trade shape. Production and TenantSetup do not
// read CommonSettings.TradeProfile themselves.
//
// Resolves IDictionaryManager at call time (ScriptServices), not via
// constructor: a captured scoped manager dies before SaveRecordAsync
// can fire CommonSettings events (ObjectDisposedException on IServiceProvider).
public partial class TradeProfileService
{
    private static IDictionaryManager Live => ScriptServices.Get<IDictionaryManager>();

    /// <summary>Unspecified, Full or BuySell. Empty settings → Unspecified.</summary>
    public async Task<string> CurrentNameAsync()
    {
        var row = (await Live.GetRecordsAsync<CommonSettings>("1 = 1", take: 1)).FirstOrDefault();
        if (row is null) return "Unspecified";
        return row.TradeProfile switch
        {
            TradeProfile.Full => "Full",
            TradeProfile.BuySell => "BuySell",
            _ => "Unspecified",
        };
    }

    public async Task<bool> IsBuySellAsync()
        => await CurrentNameAsync() == "BuySell";

    /// <summary>Stamps BuySell unless the tenant already chose Full.</summary>
    public async Task EnsureBuySellAsync()
    {
        if (await CurrentNameAsync() == "Full") return;
        await SetAsync("BuySell");
    }

    public async Task SetAsync(string profileName)
    {
        var value = Parse(profileName);
        var row = (await Live.GetRecordsAsync<CommonSettings>("1 = 1", take: 1)).FirstOrDefault()
            ?? Live.NewRecord<CommonSettings>();
        row.TradeProfile = value;
        await Live.SaveRecordAsync(row);
    }

    /// <summary>Null when production is allowed.</summary>
    public async Task<string?> ProductionBlockReasonAsync()
        => await IsBuySellAsync()
            ? "Профиль «купил-продал»: производство выключено. Смените профиль в общих настройках, если нужен выпуск."
            : null;

    private static TradeProfile Parse(string? name)
    {
        if (string.Equals(name, "Full", StringComparison.OrdinalIgnoreCase))
            return TradeProfile.Full;
        if (string.Equals(name, "BuySell", StringComparison.OrdinalIgnoreCase))
            return TradeProfile.BuySell;
        if (string.Equals(name, "Unspecified", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(name))
            return TradeProfile.Unspecified;
        throw new InvalidOperationException(
            $"Неизвестный профиль торговли «{name}». Допустимы Unspecified, Full, BuySell.");
    }
}
