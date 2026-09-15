#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class TaxRuleActionEventHandler : TypedDictionaryEventHandler<TaxRuleAction>
{
    public override async Task<EventResult> OnBeforeSaveAsync(TaxRuleAction record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;
        if (record.TaxRule == Guid.Empty)
            return EventResult.Cancel("Укажите правило");
        if (record.TaxCode == Guid.Empty)
            return EventResult.Cancel("Укажите налоговый код действия");
        if (record.RateOverride < 0m)
            return EventResult.Cancel("Ставка вручную не может быть отрицательной");
        return EventResult.Ok();
    }
}
