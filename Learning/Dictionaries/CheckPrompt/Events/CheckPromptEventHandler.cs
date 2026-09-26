#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class CheckPromptEventHandler : TypedDictionaryEventHandler<CheckPrompt>
{
    public override Task<EventResult> OnBeforeSaveAsync(CheckPrompt record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
            record.Name = "q." + Guid.NewGuid().ToString("N").Substring(0, 8);
        return Task.FromResult(EventResult.Ok());
    }

    public override async Task<EventResult> OnAfterSaveAsync(CheckPrompt record, bool isNew, EventContext context)
    {
        if (record.UnitRevision != Guid.Empty || record.Unit == Guid.Empty) return EventResult.Ok();
        var unit = await context.GetService<IDictionaryManager>().GetRecordAsync<Unit>(record.Unit);
        if (unit != null && !string.IsNullOrWhiteSpace(unit.StableId))
            await context.GetService<ILearningCatalog>().PublishLessonAsync(unit.StableId);
        return EventResult.Ok();
    }
}
