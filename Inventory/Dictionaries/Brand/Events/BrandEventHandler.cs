#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class BrandEventHandler : TypedDictionaryEventHandler<Brand>
{
    public override Task<EventResult> OnBeforeSaveAsync(Brand record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
            return Task.FromResult(EventResult.Cancel("Brand name is required"));
        return Task.FromResult(EventResult.Ok());
    }
}
