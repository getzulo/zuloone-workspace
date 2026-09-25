#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Лист сверяется целиком. Значок модуля — когда верны все вопросы этой версии.
// Верный вариант уходит в ответ только после сдачи. Сертификат отсюда не создаётся.
public partial class KnowledgeCheck
{
    private readonly IDictionaryManager _dictionaries;
    private readonly IInformationRegisterService _info;
    private readonly ILinkTableManager _links;

    public KnowledgeCheck(IDictionaryManager dictionaries, IInformationRegisterService info, ILinkTableManager links)
    {
        _dictionaries = dictionaries;
        _info = info;
        _links = links;
    }

    /// <summary>
    /// Grade a whole check. answersJson is [{prompt, option}] using the stable names.
    /// Wrong answers can be sent again. The key is included only after a pass.
    /// </summary>
    public async Task<string> SubmitAsync(string email, string stableId, string answersJson)
    {
        const string empty = "{\"passed\":false,\"badge\":false,\"wrong\":[]}";
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(stableId)) return empty;

        var unit = (await _dictionaries.GetRecordsAsync<Unit>($"StableId = '{Esc(stableId)}'")).FirstOrDefault();
        if (unit == null || unit.PublishedRevision == Guid.Empty) return empty;
        var page = await _dictionaries.GetRecordAsync<UnitRevision>(unit.PublishedRevision);
        if (page == null) return empty;

        var learner = (await _dictionaries.GetRecordsAsync<Learner>($"Email = '{Esc(email)}'")).FirstOrDefault();
        if (learner == null)
        {
            learner = _dictionaries.NewRecord<Learner>();
            learner.Email = email;
            learner.Name = email;
            learner = await _dictionaries.SaveRecordAsync(learner);
        }

        var prompts = (await _dictionaries.GetRecordsAsync<CheckPrompt>($"UnitRevision = '{page.MetaId}'"))
            .OrderBy(row => row.SortOrder)
            .ToList();
        var chosen = ParseAnswers(answersJson);
        var wrong = new List<string>();
        var passed = prompts.Count > 0;
        foreach (var prompt in prompts)
        {
            chosen.TryGetValue(prompt.Name, out var optionName);
            var options = await _dictionaries.GetRecordsAsync<CheckOption>($"Prompt = '{prompt.MetaId}'");
            var option = options.FirstOrDefault(item => string.Equals(item.Name, optionName, StringComparison.Ordinal));
            if (option == null || !option.IsCorrect)
            {
                passed = false;
                wrong.Add(prompt.Name);
            }
        }

        await _info.SetAsync(
            "LearningEvent",
            DateTime.UtcNow,
            new Dictionary<string, object?>
            {
                ["Learner"] = learner.MetaId,
                ["UnitRevision"] = page.MetaId,
            },
            new Dictionary<string, object?>
            {
                // Число, не enum: Npgsql не пишет сгенерированный тип.
                ["Kind"] = (int)(passed ? LearningEventKind.CheckPassed : LearningEventKind.CheckFailed),
            });

        if (!passed)
            return "{\"passed\":false,\"badge\":false,\"wrong\":[" + string.Join(",", wrong.Select(Quote)) + "]}";

        if (page.ModuleRevision != Guid.Empty)
        {
            await _info.SetAsync(
                "ModuleAward",
                DateTime.UtcNow,
                new Dictionary<string, object?>
                {
                    ["Learner"] = learner.MetaId,
                    ["ModuleRevision"] = page.ModuleRevision,
                },
                new Dictionary<string, object?> { ["Awarded"] = true });
            await AwardTracksAsync(learner.MetaId, page.ModuleRevision);
        }

        var key = new List<string>();
        foreach (var prompt in prompts)
        {
            var options = await _dictionaries.GetRecordsAsync<CheckOption>($"Prompt = '{prompt.MetaId}'");
            var right = options.FirstOrDefault(item => item.IsCorrect);
            if (right != null)
                key.Add("{\"prompt\":" + Quote(prompt.Name) + ",\"option\":" + Quote(right.Name) + "}");
        }
        return "{\"passed\":true,\"badge\":true,\"wrong\":[],\"key\":[" + string.Join(",", key) + "]}";
    }

    private async Task AwardTracksAsync(Guid learnerId, Guid moduleRevisionId)
    {
        var mine = await _links.GetRecordsAsync<LT_TrackRevisionModule>(
            new Dictionary<string, object?> { ["ModuleRevision"] = moduleRevisionId });
        foreach (var link in mine)
        {
            var trackRevisionId = link.TrackRevision ?? Guid.Empty;
            if (trackRevisionId == Guid.Empty) continue;
            var siblings = await _links.GetRecordsAsync<LT_TrackRevisionModule>(
                new Dictionary<string, object?> { ["TrackRevision"] = trackRevisionId });
            var complete = siblings.Count > 0;
            foreach (var sibling in siblings)
            {
                var siblingModule = sibling.ModuleRevision ?? Guid.Empty;
                var award = await _info.SliceLastAsync(
                    "ModuleAward",
                    DateTime.UtcNow.AddMinutes(1),
                    new Dictionary<string, object?>
                    {
                        ["Learner"] = learnerId,
                        ["ModuleRevision"] = siblingModule,
                    });
                if (award.Count == 0 || !AsBool(award[0], "Awarded"))
                {
                    complete = false;
                    break;
                }
            }
            if (!complete) continue;
            await _info.SetAsync(
                "TrackAward",
                DateTime.UtcNow,
                new Dictionary<string, object?>
                {
                    ["Learner"] = learnerId,
                    ["TrackRevision"] = trackRevisionId,
                },
                new Dictionary<string, object?> { ["Awarded"] = true });
        }
    }

    private static Dictionary<string, string> ParseAnswers(string json)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return map;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var prompt = Str(row, "prompt");
            if (prompt == "") continue;
            map[prompt] = Str(row, "option");
        }
        return map;
    }

    private static bool AsBool(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value == null) return false;
        if (value is bool flag) return flag;
        var text = value.ToString();
        return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1";
    }

    private static string Str(JsonElement node, string name)
        => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string Quote(string value)
        => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Esc(string value) => (value ?? "").Replace("'", "''");

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
                ["Kind"] = (int)(correct ? LearningEventKind.CheckPassed : LearningEventKind.CheckFailed),
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
