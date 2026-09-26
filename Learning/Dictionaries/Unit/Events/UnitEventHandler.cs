#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class UnitEventHandler : TypedDictionaryEventHandler<Unit>
{
    public override Task<EventResult> OnBeforeSaveAsync(Unit record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.StableId))
            record.StableId = "learn." + Guid.NewGuid().ToString("N").Substring(0, 12);
        if (record.Kind == UnitKind.Unspecified)
            record.Kind = UnitKind.Lesson;
        return Task.FromResult(EventResult.Ok());
    }

    public override async Task<EventResult> OnAfterSaveAsync(Unit record, bool isNew, EventContext context)
    {
        if (!string.IsNullOrWhiteSpace(record.StableId) && record.Module != Guid.Empty)
            await context.GetService<ILearningCatalog>().PublishLessonAsync(record.StableId);
        return EventResult.Ok();
    }
}
