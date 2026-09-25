#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Dated standard unit cost. Overlapping windows of the same item are
// refused here (input); the service still returns 0 when nothing covers
// the date — same split as TaxRate.
public partial class StandardCostEventHandler : TypedDictionaryEventHandler<StandardCost>
{
    public override async Task<EventResult> OnBeforeCreateAsync(StandardCost record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("StandardCost");
        if (record.EffectiveFrom.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "EffectiveFrom");
            if (createDay.Year >= 1902) record.EffectiveFrom = createDay;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(
        StandardCost record, bool isNew, EventContext context)
    {
        if (record.Item == Guid.Empty)
            return EventResult.Cancel("Укажите номенклатуру норматива");
        if (record.Amount < 0m)
            return EventResult.Cancel("Норматив не может быть отрицательным");
        if (record.EffectiveTo.HasValue && record.EffectiveFrom > record.EffectiveTo.Value)
            return EventResult.Cancel(
                "Окно норматива задано наоборот: дата начала позже даты окончания");

        var overlap = await context.GetService<IStandardCostService>()
            .FindOverlappingAsync(
                record.Item,
                isNew ? Guid.Empty : record.MetaId,
                record.EffectiveFrom,
                record.EffectiveTo);
        if (overlap is not null)
            return EventResult.Cancel(
                "На этот период уже есть норматив этой номенклатуры");

        return EventResult.Ok();
    }
}
