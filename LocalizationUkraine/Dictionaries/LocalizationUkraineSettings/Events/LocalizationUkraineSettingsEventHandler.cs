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
        LocalizationUkraineSettings? stored = null;
        if (!isNew && record.MetaId != Guid.Empty)
            stored = await context.GetService<IDictionaryManager<LocalizationUkraineSettings>>()
                .GetRecordAsync(record.MetaId);
        if (isNew ? AllCodesBlank(record) : stored is not null && AllCodesBlank(stored))
            await FillFromSeedAsync(record, context);

        var codes = context.GetService<IDictionaryManager<TaxCode>>();
        await AlignAsync(codes, stored?.ExemptVat ?? Guid.Empty, stored?.ExemptVatCode,
            record.ExemptVat, record.ExemptVatCode, id => record.ExemptVat = id, code => record.ExemptVatCode = code);
        await AlignAsync(codes, stored?.IncomeTax ?? Guid.Empty, stored?.IncomeTaxCode,
            record.IncomeTax, record.IncomeTaxCode, id => record.IncomeTax = id, code => record.IncomeTaxCode = code);
        await AlignAsync(codes, stored?.MilitaryLevy ?? Guid.Empty, stored?.MilitaryLevyCode,
            record.MilitaryLevy, record.MilitaryLevyCode, id => record.MilitaryLevy = id, code => record.MilitaryLevyCode = code);
        await AlignAsync(codes, stored?.SingleTax3 ?? Guid.Empty, stored?.SingleTaxCode3,
            record.SingleTax3, record.SingleTaxCode3, id => record.SingleTax3 = id, code => record.SingleTaxCode3 = code);
        await AlignAsync(codes, stored?.SingleTax5 ?? Guid.Empty, stored?.SingleTaxCode5,
            record.SingleTax5, record.SingleTaxCode5, id => record.SingleTax5 = id, code => record.SingleTaxCode5 = code);
        await AlignAsync(codes, stored?.SingleTaxExcess ?? Guid.Empty, stored?.SingleTaxCodeExcess,
            record.SingleTaxExcess, record.SingleTaxCodeExcess, id => record.SingleTaxExcess = id, code => record.SingleTaxCodeExcess = code);
        await AlignAsync(codes, stored?.SingleTaxDouble3 ?? Guid.Empty, stored?.SingleTaxCodeDouble3,
            record.SingleTaxDouble3, record.SingleTaxCodeDouble3, id => record.SingleTaxDouble3 = id, code => record.SingleTaxCodeDouble3 = code);
        await AlignAsync(codes, stored?.SingleTaxDouble5 ?? Guid.Empty, stored?.SingleTaxCodeDouble5,
            record.SingleTaxDouble5, record.SingleTaxCodeDouble5, id => record.SingleTaxDouble5 = id, code => record.SingleTaxCodeDouble5 = code);
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

    private static async Task AlignAsync(
        IDictionaryManager<TaxCode> codes,
        Guid storedId, string? storedCode, Guid currentId, string? currentCode,
        Action<Guid> setId, Action<string> setCode)
    {
        var refChanged = currentId != storedId;
        var codeChanged = (currentCode ?? "") != (storedCode ?? "");
        if (refChanged && currentId != Guid.Empty)
        {
            var stamped = await CodeOfAsync(codes, currentId, currentCode);
            if (!string.IsNullOrWhiteSpace(stamped)) setCode(stamped!);
            return;
        }
        if (codeChanged && currentId != Guid.Empty)
        {
            var refCode = await CodeOfAsync(codes, currentId, null);
            if ((refCode ?? "") != (currentCode ?? ""))
                setId(await IdOfAsync(codes, Guid.Empty, currentCode));
            return;
        }
        if (currentId != Guid.Empty && string.IsNullOrWhiteSpace(currentCode))
        {
            var stamped = await CodeOfAsync(codes, currentId, currentCode);
            if (!string.IsNullOrWhiteSpace(stamped)) setCode(stamped!);
        }
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
