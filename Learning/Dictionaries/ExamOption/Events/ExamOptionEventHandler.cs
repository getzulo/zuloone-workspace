#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class ExamOptionEventHandler : TypedDictionaryEventHandler<ExamOption>
{
    public override Task<EventResult> OnBeforeSaveAsync(ExamOption record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
            record.Name = "opt." + Guid.NewGuid().ToString("N").Substring(0, 8);
        return Task.FromResult(EventResult.Ok());
    }

    public override async Task<EventResult> OnAfterSaveAsync(ExamOption record, bool isNew, EventContext context)
    {
        var question = await context.GetService<IDictionaryManager>().GetRecordAsync<ExamQuestion>(record.Question);
        if (question == null || question.ExamRevision != Guid.Empty || question.Exam == Guid.Empty)
            return EventResult.Ok();
        await context.GetService<IExamSession>().PublishFromCardAsync(question.Exam);
        return EventResult.Ok();
    }
}
