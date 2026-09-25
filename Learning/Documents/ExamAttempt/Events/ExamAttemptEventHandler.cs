#nullable enable
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Сданный подтип порождает сертификат. Идущая и несданная попытка — нет.
public partial class ExamAttemptEventHandler : TypedDocumentEventHandler<ExamAttempt>
{
    public override async Task<EventResult> OnAfterPostAsync(ExamAttempt header, EventContext context)
    {
        if (header.Subtype != ExamAttempt.Subtypes.Passed)
            return EventResult.Ok();

        await context.GetService<IExamSession>().IssueCertificateAsync(header.MetaId);
        return EventResult.Ok();
    }
}
