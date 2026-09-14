#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class BrandEventHandler : TypedDictionaryEventHandler<Brand>
{
    public override async Task<EventResult> OnBeforeSaveAsync(Brand record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Brand name is required");
        return EventResult.Ok();
    }
}
