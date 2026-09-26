#nullable enable
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class ExamEventHandler : TypedDictionaryEventHandler<Exam>
{
    public override Task<EventResult> OnBeforeSaveAsync(Exam record, bool isNew, EventContext context)
    {
        if (record.PassPercent <= 0) record.PassPercent = 70;
        if (record.ValidDays <= 0) record.ValidDays = 365;
        return Task.FromResult(EventResult.Ok());
    }

    public override async Task<EventResult> OnAfterSaveAsync(Exam record, bool isNew, EventContext context)
    {
        if (!string.IsNullOrWhiteSpace(record.Name))
            await context.GetService<IExamSession>().PublishFromCardAsync(record.MetaId);
        return EventResult.Ok();
    }
}
