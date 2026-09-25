#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;

// Каждое открытие — новый период. Прошлые строки остаются историей.
public partial class LearningProgress
{
    private readonly IInformationRegisterService _info;

    public LearningProgress(IInformationRegisterService info) => _info = info;

    /// <summary>Record that the learner opened this page revision.</summary>
    public Task OpenAsync(Guid learnerId, Guid unitRevisionId)
    {
        return _info.SetAsync(
            "LearningEvent",
            DateTime.UtcNow,
            new Dictionary<string, object?>
            {
                ["Learner"] = learnerId,
                ["UnitRevision"] = unitRevisionId,
            },
            new Dictionary<string, object?>
            {
                ["Kind"] = LearningEventKind.Opened,
            });
    }
}
