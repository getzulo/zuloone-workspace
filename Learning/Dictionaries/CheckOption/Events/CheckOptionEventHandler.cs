#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class CheckOptionEventHandler : TypedDictionaryEventHandler<CheckOption>
{
    public override async Task<EventResult> OnBeforeSaveAsync(CheckOption record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
            record.Name = "opt." + Guid.NewGuid().ToString("N").Substring(0, 8);
        if (record.Lesson == Guid.Empty && record.Prompt != Guid.Empty)
        {
            var prompt = await context.GetService<IDictionaryManager>().GetRecordAsync<CheckPrompt>(record.Prompt);
            if (prompt != null) record.Lesson = prompt.Unit;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterSaveAsync(CheckOption record, bool isNew, EventContext context)
    {
        var prompt = await context.GetService<IDictionaryManager>().GetRecordAsync<CheckPrompt>(record.Prompt);
        if (prompt == null || prompt.UnitRevision != Guid.Empty || prompt.Unit == Guid.Empty)
            return EventResult.Ok();
        var unit = await context.GetService<IDictionaryManager>().GetRecordAsync<Unit>(prompt.Unit);
        if (unit != null && !string.IsNullOrWhiteSpace(unit.StableId))
            await context.GetService<ILearningCatalog>().PublishLessonAsync(unit.StableId);
        return EventResult.Ok();
    }
}
