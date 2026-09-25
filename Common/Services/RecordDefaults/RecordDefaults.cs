#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Events;
using ZuloOne.Services.Contracts;

// Новая карточка берёт валюту, юрлицо, ячейку выпуска, срок оплаты и даты
// начала из настроек модулей. Конец периода, факт и отметки времени сюда не
// входят: их заполняет операция, а не открытие формы.
//
// ForNewAsync гоняет OnBeforeCreate и потом дописывает ещё пустые ключи.
// Обработчик зовёт SeedAsync, не ForNewAsync: иначе открытие карточки
// зациклится на своём же событии.
public partial class RecordDefaults
{
    private static readonly string[] StartDates =
    {
        "EffectiveFrom", "DateFrom", "HireDate", "CountDate",
        "DeliveryDate", "TaxPointDate", "PeriodFrom",
    };

    public async Task<Dictionary<string, object?>> SeedAsync(string objectName)
    {
        var seed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var today = DateTime.UtcNow.Date;

        var currency = await CurrencyAsync();
        if (currency != Guid.Empty) seed["Currency"] = currency;

        var legalEntity = await RefAsync("OrganizationSettings", "DefaultLegalEntity", "DefaultLegalEntityCode", "LegalEntity");
        if (legalEntity != Guid.Empty) seed["LegalEntity"] = legalEntity;

        var output = await RefAsync("ProductionSettings", "DefaultOutputLocation", "DefaultOutputLocationCode", "StoreCell");
        if (output != Guid.Empty) seed["OutputLocation"] = output;

        var termTable = PurchaseSide(objectName) ? "PurchasingSettings" : "SalesSettings";
        var term = await TermAsync(termTable);
        if (term != Guid.Empty) seed["PaymentTerm"] = term;

        foreach (var name in StartDates) seed[name] = today;

        var due = today;
        if (term != Guid.Empty)
        {
            var days = await Resolve<IPaymentDueService>().DaysOfAsync(term);
            due = Resolve<IPaymentDueService>().DueOn(today, days);
        }
        seed["DueDate"] = due;
        return seed;
    }

    /// <summary>
    /// Пустой Guid и срок, совпавший с умолчанием модуля, ещё не выбор пользователя.
    /// Условие клиента или договора подставляется поверх.
    /// </summary>
    public bool IsPlaceholder(Guid current, Guid moduleDefault)
        => current == Guid.Empty || (moduleDefault != Guid.Empty && current == moduleDefault);

    public Guid Pick(Dictionary<string, object?> seed, string field)
    {
        if (!seed.TryGetValue(field, out var value) || value == null) return Guid.Empty;
        if (value is Guid id) return id;
        return Guid.TryParse(value.ToString(), out var parsed) ? parsed : Guid.Empty;
    }

    public DateTime PickDay(Dictionary<string, object?> seed, string field)
    {
        if (!seed.TryGetValue(field, out var value) || value == null) return default;
        if (value is DateTime day) return day;
        return DateTime.TryParse(value.ToString(), out var parsed) ? parsed : default;
    }

    public async Task<Dictionary<string, object?>> ForNewAsync(string objectName)
    {
        var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var kind = await KindAsync(objectName);
        if (kind != null)
            await Resolve<EventExecutor>().ExecuteAsync(kind, objectName, EventType.OnBeforeCreate, bag);

        var seed = await SeedAsync(objectName);
        foreach (var pair in seed)
        {
            if (!bag.TryGetValue(pair.Key, out var existing) || Empty(existing))
                bag[pair.Key] = pair.Value;
        }
        if (kind == "Document" && (!bag.TryGetValue("DocumentDate", out var date) || Empty(date)))
            bag["DocumentDate"] = DateTime.UtcNow.Date;
        return bag;
    }

    private static bool PurchaseSide(string objectName)
        => objectName is "PurchaseOrder" or "PurchaseCreditNote" or "VendorPayment" or "Vendor";

    private async Task<string?> KindAsync(string objectName)
    {
        var meta = Resolve<IMetadataService>();
        if ((await meta.GetAllDocumentTypesAsync()).Any(d => d.Name == objectName)) return "Document";
        if ((await meta.GetAllDictionariesAsync()).Any(d => d.Name == objectName)) return "Dictionary";
        return null;
    }

    private async Task<Guid> CurrencyAsync()
    {
        var common = await RefAsync("CommonSettings", "DefaultCurrency", "DefaultCurrencyCode", "Currency");
        if (common != Guid.Empty) return common;
        return await RefAsync("AccountingSettings", "DefaultCurrency", "DefaultCurrencyCode", "Currency");
    }

    private async Task<Guid> TermAsync(string settingsTable)
    {
        var row = await FirstAsync(settingsTable);
        if (row == null) return Guid.Empty;
        var id = GuidOf(row, "DefaultPaymentTerm");
        if (id != Guid.Empty) return id;
        var days = IntOf(row, "DefaultPaymentTermDays");
        if (days <= 0) return Guid.Empty;
        var found = await FirstAsync("PaymentTerm", $"Days = {days}");
        return found == null ? Guid.Empty : GuidOf(found, "MetaId");
    }

    private async Task<Guid> RefAsync(string settingsTable, string guidField, string codeField, string targetTable)
    {
        var row = await FirstAsync(settingsTable);
        if (row == null) return Guid.Empty;
        var id = GuidOf(row, guidField);
        if (id != Guid.Empty) return id;
        var code = TextOf(row, codeField);
        if (string.IsNullOrWhiteSpace(code)) return Guid.Empty;
        var key = code.Replace("'", "''");
        var filter = targetTable == "Currency"
            ? $"ID = '{key}' OR Code = '{key}'"
            : $"ID = '{key}'";
        var found = await FirstAsync(targetTable, filter);
        return found == null ? Guid.Empty : GuidOf(found, "MetaId");
    }

    private static async Task<Dictionary<string, object?>?> FirstAsync(string table, string? filter = null)
    {
        var rows = await Resolve<IDataService>().QueryAsync(table, filter, take: 1);
        return rows.FirstOrDefault();
    }

    private static bool Empty(object? value)
    {
        if (value == null) return true;
        if (value is Guid id) return id == Guid.Empty;
        if (value is DateTime day) return day.Year < 1902;
        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (Guid.TryParse(text, out var parsed) && parsed == Guid.Empty) return true;
        return DateTime.TryParse(text, out var parsedDay) && parsedDay.Year < 1902;
    }

    private static Guid GuidOf(IDictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value == null) return Guid.Empty;
        if (value is Guid id) return id;
        return Guid.TryParse(value.ToString(), out var parsed) ? parsed : Guid.Empty;
    }

    private static int IntOf(IDictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value == null) return 0;
        if (value is int n) return n;
        if (value is long l) return (int)l;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    private static string? TextOf(IDictionary<string, object?> row, string field)
        => row.TryGetValue(field, out var value) ? value?.ToString() : null;

    private static T Resolve<T>() where T : class
    {
        var ambient = ScriptServices.AmbientScope;
        if (ambient?.GetService(typeof(T)) is T scoped) return scoped;
        return ScriptServices.Get<T>();
    }
}
