#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

// Тонкая дверь сайта обучения. Верный вариант в ответ не кладётся.
public partial class LearningApiService
{
    public override async Task<object?> Any(LearningApiRequest request)
    {
        var progress = GetService<ILearningProgress>();
        if (string.Equals(request.Action, "place", StringComparison.Ordinal))
        {
            return new LearningApiResponse
            {
                Ok = true,
                Error = "",
                Payload = await progress.PlaceAsync(request.Email ?? ""),
            };
        }

        var exams = GetService<IExamSession>();
        if (string.Equals(request.Action, "exam", StringComparison.Ordinal))
        {
            return new LearningApiResponse
            {
                Ok = true,
                Error = "",
                Payload = await exams.AskAsync(request.Email ?? "", request.StableId ?? ""),
            };
        }

        if (string.Equals(request.Action, "exam-answer", StringComparison.Ordinal))
        {
            return new LearningApiResponse
            {
                Ok = true,
                Error = "",
                Payload = await exams.ReplyAsync(request.Email ?? "", request.StableId ?? "", request.Answers ?? ""),
            };
        }

        if (string.Equals(request.Action, "assign", StringComparison.Ordinal))
        {
            return new LearningApiResponse
            {
                Ok = true,
                Error = "",
                Payload = await GetService<ILearningReport>().AssignAsync(
                    request.Email ?? "", request.StableId ?? "", request.Answers ?? ""),
            };
        }

        if (string.Equals(request.Action, "report", StringComparison.Ordinal))
        {
            return new LearningApiResponse
            {
                Ok = true,
                Error = "",
                Payload = await GetService<ILearningReport>().ForStandAsync(
                    request.Email ?? "", request.StableId ?? ""),
            };
        }

        if (string.Equals(request.Action, "answer", StringComparison.Ordinal))
        {
            return new LearningApiResponse
            {
                Ok = true,
                Error = "",
                Payload = await GetService<IKnowledgeCheck>().SubmitAsync(
                    request.Email ?? "", request.StableId ?? "", request.Answers ?? ""),
            };
        }

        if (!string.Equals(request.Action, "open", StringComparison.Ordinal))
            return new LearningApiResponse { Ok = false, Error = "Неизвестное действие", Payload = "" };

        var stableId = request.StableId ?? "";
        var revision = await progress.OpenByEmailAsync(request.Email ?? "", stableId);
        if (revision == Guid.Empty)
            return new LearningApiResponse { Ok = false, Error = "Страница не опубликована", Payload = "" };

        return new LearningApiResponse
        {
            Ok = true,
            Error = "",
            Payload = await GetService<ILearningCatalog>().ReadPageAsync(stableId),
        };
    }
}
