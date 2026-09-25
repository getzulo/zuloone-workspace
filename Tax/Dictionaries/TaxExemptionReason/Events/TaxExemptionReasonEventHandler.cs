#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Catalog of why a supply is exempt. (Tax, Code) is unique: two rows with
// the same code on one tax would make the stamp on TaxCalculation ambiguous.
public partial class TaxExemptionReasonEventHandler : TypedDictionaryEventHandler<TaxExemptionReason>
{
    public override async Task<EventResult> OnBeforeCreateAsync(TaxExemptionReason record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("TaxExemptionReason");
        if (record.EffectiveFrom.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "EffectiveFrom");
            if (createDay.Year >= 1902) record.EffectiveFrom = createDay;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(
        TaxExemptionReason record, bool isNew, EventContext context)
    {
        if (record.Tax == Guid.Empty)
            return EventResult.Cancel("Укажите налог причины освобождения");
        if (string.IsNullOrWhiteSpace(record.Code))
            return EventResult.Cancel("Укажите код причины освобождения");
        if (record.EffectiveTo.HasValue && record.EffectiveFrom > record.EffectiveTo.Value)
            return EventResult.Cancel(
                "Окно действия задано наоборот: дата начала позже даты окончания");

        var code = record.Code.Trim();
        record.Code = code;
        var others = await context.GetService<IDictionaryManager<TaxExemptionReason>>()
            .GetRecordsAsync($"Tax = '{record.Tax}'");
        var clash = others.FirstOrDefault(r =>
            r.MetaId != record.MetaId
            && string.Equals(r.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
            return EventResult.Cancel(
                $"Код «{code}» уже есть у этого налога");

        return EventResult.Ok();
    }
}
