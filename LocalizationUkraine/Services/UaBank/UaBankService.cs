#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Core.Services.Integration;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Service "UaBank": виписка ПриватБанка через Autoclient API v3.0.0.
//
// Контракт: https://acp.privatbank.ua — GET /api/statements/transactions
// (опис API з кабінету «Інтеграція з Приват24 для бізнесу»).
//
// Скрипт HTTP не відкриває: канал pb-statements, authMode Header, заголовок
// token. Тіло на GET хост не ставить (Autoclient інакше 400).
//
// Унікальність проводки — REF+REFN, як написано в офіційному описі.
// CustomerPayment / VendorPayment з виписки НЕ створюються: без звірки ЄДРПОУ
// це чужі гроші на чужому договорі. Платіж create + КЕП add-sign — окремий зріз.
public partial class UaBank
{
    private const string Channel = "pb-statements";
    private const string UserAgent = "ZuloOne-LocalizationUkraine";
    private const int PageLimit = 100;
    private const int MaxPages = 50;
    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(1100);

    private readonly IOutboundCall _call;
    private readonly IDictionaryManager<UaBankConnection> _connections;
    private readonly IInformationRegisterService _info;

    public UaBank(
        IOutboundCall call,
        IDictionaryManager<UaBankConnection> connections,
        IInformationRegisterService info)
    {
        _call = call;
        _connections = connections;
        _info = info;
    }

    /// <summary>Усі робочі підключення за вчора. Завдання PullUaBankStatements.</summary>
    public async Task<int> PullAllAsync()
    {
        var imported = 0;
        foreach (var row in await _connections.GetRecordsAsync("1 = 1"))
        {
            if (row.IsDisabled) continue;
            imported += await PullConnectionAsync(row.MetaId);
        }
        return imported;
    }

    /// <summary>Одне підключення: settings (phase WRK) → сторінки transactions.</summary>
    public async Task<int> PullConnectionAsync(Guid connectionId)
    {
        var connection = await _connections.GetRecordAsync(connectionId);
        if (connection is null || connection.IsDisabled || connection.LegalEntity == Guid.Empty)
            return 0;
        var code = Convert.ToString(connection.Code);
        if (string.IsNullOrWhiteSpace(code)) return 0;

        var settings = await CallAsync(code, "/statements/settings", "pb-settings:" + code);
        if (settings is null || !CanPull(settings)) return 0;
        await Task.Delay(Pace);

        var day = DateTime.UtcNow.Date.AddDays(-1).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var acc = Convert.ToString(connection.AccountNumber) ?? "";
        var followId = "";
        var imported = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var path = "/statements/transactions?startDate=" + EscapeQuery(day)
                + "&endDate=" + EscapeQuery(day)
                + "&limit=" + PageLimit;
            if (!string.IsNullOrWhiteSpace(acc))
                path += "&acc=" + EscapeQuery(acc);
            if (!string.IsNullOrWhiteSpace(followId))
                path += "&followId=" + EscapeQuery(followId);

            var body = await CallAsync(code, path, "pb-tx:" + code + ":" + page);
            if (body is null) break;
            imported += await ImportParsedAsync(connection.LegalEntity, body);
            if (!TryNextPage(body, out followId)) break;
            await Task.Delay(Pace);
        }

        return imported;
    }

    /// <summary>
    /// Розбір офіційного JSON і запис регістра. Тести годують цим методом
    /// зразок з опису API, без живого acp.privatbank.ua.
    /// </summary>
    public async Task<int> ImportBodyAsync(Guid connectionId, string body)
    {
        var connection = await _connections.GetRecordAsync(connectionId);
        if (connection is null || connection.LegalEntity == Guid.Empty) return 0;
        return await ImportParsedAsync(connection.LegalEntity, body);
    }

    private async Task<int> ImportParsedAsync(Guid legalEntity, string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (JsonException)
        {
            return 0;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("transactions", out var rows)
                || rows.ValueKind != JsonValueKind.Array)
                return 0;

            var imported = 0;
            foreach (var row in rows.EnumerateArray())
            {
                if (!IsPosted(row)) continue;
                var bankRef = Str(row, "REF") + Str(row, "REFN");
                if (string.IsNullOrWhiteSpace(Str(row, "REF"))) continue;

                var seen = await _info.SliceLastAsync(
                    "UaBankStatement",
                    DateTime.UtcNow,
                    new Dictionary<string, object?>
                    {
                        ["LegalEntity"] = legalEntity,
                        ["BankRef"] = bankRef,
                    });
                if (seen.Count > 0) continue;

                await _info.SetAsync(
                    "UaBankStatement",
                    PeriodOf(row),
                    new Dictionary<string, object?>
                    {
                        ["LegalEntity"] = legalEntity,
                        ["BankRef"] = bankRef,
                    },
                    new Dictionary<string, object?>
                    {
                        ["Amount"] = AmountOf(row),
                        ["TranType"] = Str(row, "TRANTYPE"),
                        ["Purpose"] = Str(row, "OSND"),
                        ["CounterpartyEdrpou"] = Str(row, "AUT_CNTR_CRF"),
                        ["CounterpartyName"] = Str(row, "AUT_CNTR_NAM"),
                    });
                imported++;
            }

            return imported;
        }
    }

    private async Task<string?> CallAsync(string connectionRef, string relativePath, string key)
    {
        var result = await _call.SendAsync(
            Channel, "",
            idempotencyKey: key,
            connectionRef: connectionRef,
            headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["User-Agent"] = UserAgent,
                ["X-ZuloOne-RelativePath"] = relativePath,
            },
            requireCredential: true);
        if (result.Ok) return result.Body ?? "";
        return null;
    }

    private static bool CanPull(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!string.Equals(Str(root, "status"), "SUCCESS", StringComparison.OrdinalIgnoreCase))
                return false;
            var settings = root.TryGetProperty("settings", out var nested) ? nested : root;
            if (!string.Equals(Str(settings, "phase"), "WRK", StringComparison.OrdinalIgnoreCase))
                return false;
            var balance = Str(settings, "work_balance");
            return !string.Equals(balance, "Y", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryNextPage(string body, out string followId)
    {
        followId = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("exist_next_page", out var flag)
                || flag.ValueKind != JsonValueKind.True)
                return false;
            followId = Str(root, "next_page_id");
            return !string.IsNullOrWhiteSpace(followId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsPosted(JsonElement row)
    {
        var real = Str(row, "FL_REAL");
        var state = Str(row, "PR_PR");
        return string.Equals(real, "r", StringComparison.OrdinalIgnoreCase)
            && string.Equals(state, "r", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal AmountOf(JsonElement row)
    {
        var raw = Str(row, "SUM");
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;
    }

    private static DateTime PeriodOf(JsonElement row)
    {
        var stamped = Str(row, "DATE_TIME_DAT_OD_TIM_P");
        if (DateTime.TryParseExact(stamped, "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var at))
            return at;
        var day = Str(row, "DAT_OD");
        if (DateTime.TryParseExact(day, "dd.MM.yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return date;
        return DateTime.UtcNow.Date;
    }

    private static string Str(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    private static string EscapeQuery(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '-' or '_' or '.' or '~')
                sb.Append(c);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
