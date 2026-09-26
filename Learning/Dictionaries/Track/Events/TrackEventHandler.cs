#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class TrackEventHandler : TypedDictionaryEventHandler<Track>
{
    public override Task<EventResult> OnBeforeSaveAsync(Track record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.StableId))
            record.StableId = "learn." + Guid.NewGuid().ToString("N").Substring(0, 12);
        return Task.FromResult(EventResult.Ok());
    }

    public override async Task<EventResult> OnAfterSaveAsync(Track record, bool isNew, EventContext context)
    {
        if (!string.IsNullOrWhiteSpace(record.StableId))
            await context.GetService<ILearningCatalog>().PublishGroupAsync(record.StableId);
        return EventResult.Ok();
    }
}
