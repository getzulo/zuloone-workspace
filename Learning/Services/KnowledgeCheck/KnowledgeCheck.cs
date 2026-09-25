#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Ответ сверяется с вариантом этой версии страницы. Значок пишется на версию
// модуля. Страница с одним вопросом закрывает модуль верным ответом.
public partial class KnowledgeCheck
{
    private readonly IDictionaryManager _dictionaries;
    private readonly IInformationRegisterService _info;

    public KnowledgeCheck(IDictionaryManager dictionaries, IInformationRegisterService info)
    {
        _dictionaries = dictionaries;
        _info = info;
    }

    /// <summary>True when the chosen option is the correct one for this prompt.</summary>
    public async Task<bool> AnswerAsync(Guid learnerId, Guid unitRevisionId, Guid promptId, Guid optionId)
    {
        var option = await _dictionaries.GetRecordAsync<CheckOption>(optionId);
        var correct = option != null && option.Prompt == promptId && option.IsCorrect;
        await _info.SetAsync(
            "LearningEvent",
            DateTime.UtcNow,
            new Dictionary<string, object?>
            {
                ["Learner"] = learnerId,
                ["UnitRevision"] = unitRevisionId,
            },
            new Dictionary<string, object?>
            {
                ["Kind"] = correct ? LearningEventKind.CheckPassed : LearningEventKind.CheckFailed,
            });

        if (!correct) return false;

        var prompts = await _dictionaries.GetRecordsAsync<CheckPrompt>($"UnitRevision = '{unitRevisionId}'");
        if (prompts.Count() != 1) return true;

        var page = await _dictionaries.GetRecordAsync<UnitRevision>(unitRevisionId);
        if (page == null || page.ModuleRevision == Guid.Empty) return true;

        await _info.SetAsync(
            "ModuleAward",
            DateTime.UtcNow,
            new Dictionary<string, object?>
            {
                ["Learner"] = learnerId,
                ["ModuleRevision"] = page.ModuleRevision,
            },
            new Dictionary<string, object?>
            {
                ["Awarded"] = true,
            });
        return true;
    }
}
