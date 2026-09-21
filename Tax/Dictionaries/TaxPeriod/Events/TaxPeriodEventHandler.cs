#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Named return window. Overlapping windows of the same legal entity and tax
// would make BuildFromPeriodAsync pick at random.
public partial class TaxPeriodEventHandler : TypedDictionaryEventHandler<TaxPeriod>
{
    public override async Task<EventResult> OnBeforeSaveAsync(
        TaxPeriod record, bool isNew, EventContext context)
    {
        if (record.LegalEntity == Guid.Empty)
            return EventResult.Cancel("Укажите юрлицо периода");
        if (record.Tax == Guid.Empty)
            return EventResult.Cancel("Укажите налог периода");
        if (string.IsNullOrWhiteSpace(record.Code))
            return EventResult.Cancel("Укажите код периода");
        if (record.ToDate.Date < record.FromDate.Date)
            return EventResult.Cancel(
                "Окно периода задано наоборот: дата начала позже даты окончания");

        var others = await context.GetService<IDictionaryManager<TaxPeriod>>()
            .GetRecordsAsync($"LegalEntity = '{record.LegalEntity}' AND Tax = '{record.Tax}'");
        var clash = others.FirstOrDefault(p =>
            p.MetaId != record.MetaId
            && WindowsOverlap(record.FromDate, record.ToDate, p.FromDate, p.ToDate));
        if (clash is not null)
            return EventResult.Cancel(
                $"Период «{clash.Code}» уже покрывает эти даты у того же юрлица и налога");

        return EventResult.Ok();
    }

    private static bool WindowsOverlap(DateTime aFrom, DateTime aTo, DateTime bFrom, DateTime bTo)
        => aFrom.Date <= bTo.Date && bFrom.Date <= aTo.Date;
}
