#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
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
        await StampAsync(record, context);
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterLoadAsync(
        LocalizationUkraineSettings record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        var codes = context.GetService<IDictionaryManager<TaxCode>>();
        record.ExemptVat = await IdOfAsync(codes, record.ExemptVat, record.ExemptVatCode);
        record.IncomeTax = await IdOfAsync(codes, record.IncomeTax, record.IncomeTaxCode);
        record.MilitaryLevy = await IdOfAsync(codes, record.MilitaryLevy, record.MilitaryLevyCode);
        record.SingleTax3 = await IdOfAsync(codes, record.SingleTax3, record.SingleTaxCode3);
        record.SingleTax5 = await IdOfAsync(codes, record.SingleTax5, record.SingleTaxCode5);
        record.SingleTaxExcess = await IdOfAsync(codes, record.SingleTaxExcess, record.SingleTaxCodeExcess);
        record.SingleTaxDouble3 = await IdOfAsync(codes, record.SingleTaxDouble3, record.SingleTaxCodeDouble3);
        record.SingleTaxDouble5 = await IdOfAsync(codes, record.SingleTaxDouble5, record.SingleTaxCodeDouble5);
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
        await SeedAsync(codes, "UA-E", record.ExemptVat, record.ExemptVatCode,
            id => record.ExemptVat = id, code => record.ExemptVatCode = code);
        await SeedAsync(codes, "UA-EP3", record.SingleTax3, record.SingleTaxCode3,
            id => record.SingleTax3 = id, code => record.SingleTaxCode3 = code);
        await SeedAsync(codes, "UA-EP5", record.SingleTax5, record.SingleTaxCode5,
            id => record.SingleTax5 = id, code => record.SingleTaxCode5 = code);
        await SeedAsync(codes, "UA-EP15", record.SingleTaxExcess, record.SingleTaxCodeExcess,
            id => record.SingleTaxExcess = id, code => record.SingleTaxCodeExcess = code);

        // Подвійна ставка (ПКУ 293.5) — превышение у ЮРЛИЦА. Ставка 15% строкой
        // выше принадлежит ФОП (рядок 07 форми F0103309); у юрособи рядок 2
        // форми J0103509 заполняется 6 % або 10 %, и UA-EP15 туда не кладётся.
        await SeedAsync(codes, "UA-EP6", record.SingleTaxDouble3, record.SingleTaxCodeDouble3,
            id => record.SingleTaxDouble3 = id, code => record.SingleTaxCodeDouble3 = code);
        await SeedAsync(codes, "UA-EP10", record.SingleTaxDouble5, record.SingleTaxCodeDouble5,
            id => record.SingleTaxDouble5 = id, code => record.SingleTaxCodeDouble5 = code);
    }

    private static async Task SeedAsync(
        IDictionaryManager<TaxCode> codes, string code, Guid currentId, string? currentCode,
        Action<Guid> setId, Action<string> setCode)
    {
        if (currentId != Guid.Empty || !string.IsNullOrWhiteSpace(currentCode)) return;
        var rows = await codes.GetRecordsAsync($"Code = '{code}'", take: 1);
        if (rows.Count == 0 || string.IsNullOrWhiteSpace(rows[0].Code)) return;
        setId(rows[0].MetaId);
        setCode(rows[0].Code!);
    }

    private static async Task StampAsync(LocalizationUkraineSettings record, EventContext context)
    {
        var codes = context.GetService<IDictionaryManager<TaxCode>>();
        if (record.ExemptVat != Guid.Empty)
            record.ExemptVatCode = await CodeOfAsync(codes, record.ExemptVat, record.ExemptVatCode);
        if (record.IncomeTax != Guid.Empty)
            record.IncomeTaxCode = await CodeOfAsync(codes, record.IncomeTax, record.IncomeTaxCode);
        if (record.MilitaryLevy != Guid.Empty)
            record.MilitaryLevyCode = await CodeOfAsync(codes, record.MilitaryLevy, record.MilitaryLevyCode);
        if (record.SingleTax3 != Guid.Empty)
            record.SingleTaxCode3 = await CodeOfAsync(codes, record.SingleTax3, record.SingleTaxCode3);
        if (record.SingleTax5 != Guid.Empty)
            record.SingleTaxCode5 = await CodeOfAsync(codes, record.SingleTax5, record.SingleTaxCode5);
        if (record.SingleTaxExcess != Guid.Empty)
            record.SingleTaxCodeExcess = await CodeOfAsync(codes, record.SingleTaxExcess, record.SingleTaxCodeExcess);
        if (record.SingleTaxDouble3 != Guid.Empty)
            record.SingleTaxCodeDouble3 = await CodeOfAsync(codes, record.SingleTaxDouble3, record.SingleTaxCodeDouble3);
        if (record.SingleTaxDouble5 != Guid.Empty)
            record.SingleTaxCodeDouble5 = await CodeOfAsync(codes, record.SingleTaxDouble5, record.SingleTaxCodeDouble5);
    }

    private static async Task<string?> CodeOfAsync(
        IDictionaryManager<TaxCode> codes, Guid id, string? fallback)
    {
        var row = await codes.GetRecordAsync(id);
        return string.IsNullOrWhiteSpace(row?.Code) ? fallback : row!.Code;
    }

    private static async Task<Guid> IdOfAsync(
        IDictionaryManager<TaxCode> codes, Guid current, string? code)
    {
        if (current != Guid.Empty || string.IsNullOrWhiteSpace(code)) return current;
        var lit = code.Replace("'", "''");
        var row = (await codes.GetRecordsAsync($"Code = '{lit}'", take: 1)).FirstOrDefault();
        return row?.MetaId ?? Guid.Empty;
    }
}
