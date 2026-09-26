#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class ExamQuestionEventHandler : TypedDictionaryEventHandler<ExamQuestion>
{
    public override Task<EventResult> OnBeforeSaveAsync(ExamQuestion record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
            record.Name = "q." + Guid.NewGuid().ToString("N").Substring(0, 8);
        return Task.FromResult(EventResult.Ok());
    }

    public override async Task<EventResult> OnAfterSaveAsync(ExamQuestion record, bool isNew, EventContext context)
    {
        if (record.ExamRevision != Guid.Empty || record.Exam == Guid.Empty) return EventResult.Ok();
        await context.GetService<IExamSession>().PublishFromCardAsync(record.Exam);
        return EventResult.Ok();
    }
}
