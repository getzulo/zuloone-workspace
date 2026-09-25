#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// One tax code, direction and return type has one box in a date window.
public partial class TaxReportMappingEventHandler : TypedDictionaryEventHandler<TaxReportMapping>
{
    public override async Task<EventResult> OnBeforeCreateAsync(TaxReportMapping record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("TaxReportMapping");
        if (record.EffectiveFrom.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "EffectiveFrom");
            if (createDay.Year >= 1902) record.EffectiveFrom = createDay;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(
        TaxReportMapping record, bool isNew, EventContext context)
    {
        if (record.Tax == Guid.Empty)
            return EventResult.Cancel("Укажите налог сопоставления");
        if (record.TaxCode == Guid.Empty)
            return EventResult.Cancel("Укажите налоговый код");
        if (string.IsNullOrWhiteSpace(record.ReturnType))
            return EventResult.Cancel("Укажите тип декларации");
        if (string.IsNullOrWhiteSpace(record.ReturnBox))
            return EventResult.Cancel("Укажите ячейку декларации");
        if (record.EffectiveTo.HasValue && record.EffectiveFrom > record.EffectiveTo.Value)
            return EventResult.Cancel(
                "Окно действия задано наоборот: дата начала позже даты окончания");

        var code = await context.GetService<IDictionaryManager<TaxCode>>()
            .GetRecordAsync(record.TaxCode);
        if (code != null && code.Tax != Guid.Empty && code.Tax != record.Tax)
            return EventResult.Cancel(
                $"Код «{code.Code}» принадлежит другому налогу");

        record.ReturnType = record.ReturnType.Trim();
        record.ReturnBox = record.ReturnBox.Trim();

        var others = await context.GetService<IDictionaryManager<TaxReportMapping>>()
            .GetRecordsAsync($"TaxCode = '{record.TaxCode}'");
        var clash = others.FirstOrDefault(m =>
            m.MetaId != record.MetaId
            && m.Direction == record.Direction
            && string.Equals(m.ReturnType, record.ReturnType, StringComparison.OrdinalIgnoreCase)
            && WindowsOverlap(record.EffectiveFrom, record.EffectiveTo, m.EffectiveFrom, m.EffectiveTo));
        if (clash is not null)
            return EventResult.Cancel(
                $"Ячейка «{clash.ReturnBox}» уже сопоставлена этому коду в том же типе декларации");

        return EventResult.Ok();
    }

    private static bool WindowsOverlap(DateTime aFrom, DateTime? aTo, DateTime bFrom, DateTime? bTo)
        => aFrom.Date <= (bTo?.Date ?? DateTime.MaxValue)
        && bFrom.Date <= (aTo?.Date ?? DateTime.MaxValue);
}
