#nullable enable
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// ═══ КОДЫ ИЗ СИДА, ЕСЛИ НАСТРОЙКИ ЕЩЁ ПУСТЫЕ ════════════════════════════════
//
// vat-UA сеет налоги, не одиночный справочник. Пустые коды означают «не
// начисляй ЄП / не заводи сопоставление освобождения». Это законно, пока
// человек так решил. Но первое касание настроек ПОСЛЕ сида — это не решение,
// а дыра: спрощенець 5% уехал бы в DefaultTaxCode, и выглядело бы как «пакет
// не работает».
//
// Смотрим на УЖЕ СОХРАНЁННУЮ строку, не на входящий мешок: OnBeforeSave на
// UPDATE гидратирует только затронутые поля, и чужие коды выглядели бы
// пустыми. Один заполненный код в базе — человек уже настраивал, чужое поле
// не дописываем: пустой ExemptVatCode рядом с UA-EP5 может быть сознательным
// «не ставь сопоставление».
public partial class LocalizationUkraineSettingsEventHandler
    : TypedDictionaryEventHandler<LocalizationUkraineSettings>
{
    public override async Task<EventResult> OnBeforeSaveAsync(
        LocalizationUkraineSettings record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;
        if (await ShouldFillAsync(record, isNew, context))
            await FillFromSeedAsync(record, context);
        return EventResult.Ok();
    }

    private static async Task<bool> ShouldFillAsync(
        LocalizationUkraineSettings record, bool isNew, EventContext context)
    {
        if (isNew) return AllCodesBlank(record);

        var current = await context
            .GetService<IDictionaryManager<LocalizationUkraineSettings>>()
            .GetRecordAsync(record.MetaId);
        return current is not null && AllCodesBlank(current);
    }

    private static bool AllCodesBlank(LocalizationUkraineSettings record)
        => string.IsNullOrWhiteSpace(record.SingleTaxCode3)
        && string.IsNullOrWhiteSpace(record.SingleTaxCode5)
        && string.IsNullOrWhiteSpace(record.SingleTaxCodeExcess)
        && string.IsNullOrWhiteSpace(record.ExemptVatCode);

    private static async Task FillFromSeedAsync(
        LocalizationUkraineSettings record, EventContext context)
    {
        var codes = context.GetService<IDictionaryManager<TaxCode>>();
        record.ExemptVatCode = await ExistingAsync(codes, "UA-E") ?? record.ExemptVatCode;
        record.SingleTaxCode3 = await ExistingAsync(codes, "UA-EP3") ?? record.SingleTaxCode3;
        record.SingleTaxCode5 = await ExistingAsync(codes, "UA-EP5") ?? record.SingleTaxCode5;
        record.SingleTaxCodeExcess = await ExistingAsync(codes, "UA-EP15") ?? record.SingleTaxCodeExcess;
    }

    private static async Task<string?> ExistingAsync(
        IDictionaryManager<TaxCode> codes, string code)
    {
        var rows = await codes.GetRecordsAsync($"Code = '{code}'", take: 1);
        return rows.Count > 0 ? code : null;
    }
}
