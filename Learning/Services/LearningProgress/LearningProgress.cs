#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Каждое открытие — новый период. Прошлые строки остаются историей.
public partial class LearningProgress
{
    private readonly IDictionaryManager _dictionaries;
    private readonly IInformationRegisterService _info;
    private readonly ILinkTableManager _links;

    public LearningProgress(IDictionaryManager dictionaries, IInformationRegisterService info, ILinkTableManager links)
    {
        _dictionaries = dictionaries;
        _info = info;
        _links = links;
    }

    /// <summary>Stable ids of published pages this email has opened. Empty when the learner does not exist yet.</summary>
    public async Task<string> PlaceAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "{\"opened\":[]}";
        var learner = (await _dictionaries.GetRecordsAsync<Learner>($"Email = '{Esc(email)}'")).FirstOrDefault();
        if (learner == null) return "{\"opened\":[]}";

        var events = await _info.SliceLastAsync(
            "LearningEvent",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?> { ["Learner"] = learner.MetaId });
        var units = await _dictionaries.GetRecordsAsync<Unit>("1 = 1");
        var opened = new List<string>();
        foreach (var row in events)
        {
            var revision = AsGuid(row, "UnitRevision");
            var unit = units.FirstOrDefault(item => item.PublishedRevision == revision);
            if (unit != null && !opened.Contains(unit.StableId))
                opened.Add(unit.StableId);
        }
        var badges = new List<string>();
        var awards = await _info.SliceLastAsync(
            "ModuleAward",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?> { ["Learner"] = learner.MetaId });
        var modules = await _dictionaries.GetRecordsAsync<Module>("1 = 1");
        foreach (var row in awards)
        {
            if (!AsBool(row, "Awarded")) continue;
            var revision = AsGuid(row, "ModuleRevision");
            var module = modules.FirstOrDefault(item => item.PublishedRevision == revision);
            if (module != null && !badges.Contains(module.StableId))
                badges.Add(module.StableId);
        }
        var assigned = new List<string>();
        var enrollments = await _info.SliceLastAsync(
            "Enrollment",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?> { ["Learner"] = learner.MetaId });
        var tracks = await _dictionaries.GetRecordsAsync<Track>("1 = 1");
        foreach (var row in enrollments)
        {
            if (!IsAssigned(row)) continue;
            var trackId = AsGuid(row, "Track");
            var track = tracks.FirstOrDefault(item => item.MetaId == trackId);
            if (track != null && !string.IsNullOrWhiteSpace(track.StableId) && !assigned.Contains(track.StableId))
                assigned.Add(track.StableId);
        }
        return "{\"opened\":[" + string.Join(",", opened.Select(Quote))
            + "],\"badges\":[" + string.Join(",", badges.Select(Quote))
            + "],\"assigned\":[" + string.Join(",", assigned.Select(Quote)) + "]}";
    }

    private static bool IsAssigned(Dictionary<string, object?> row)
    {
        if (!row.TryGetValue("Source", out var value) || value == null) return false;
        if (value is int number) return number == (int)EnrollmentSource.Assigned;
        var text = value.ToString() ?? "";
        return text == "2" || text == "Assigned";
    }

    private static bool AsBool(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value == null) return false;
        if (value is bool flag) return flag;
        var text = value.ToString();
        return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1";
    }

    private static string Quote(string value) => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>
    /// Open the published page for this email. Creates the learner on the first open.
    /// Returns the page revision id, or empty when the page is not published.
    /// </summary>
    public async Task<Guid> OpenByEmailAsync(string email, string stableId)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(stableId))
            return Guid.Empty;

        var unit = (await _dictionaries.GetRecordsAsync<Unit>($"StableId = '{Esc(stableId)}'")).FirstOrDefault();
        if (unit == null || unit.PublishedRevision == Guid.Empty)
            return Guid.Empty;

        var learner = (await _dictionaries.GetRecordsAsync<Learner>($"Email = '{Esc(email)}'")).FirstOrDefault();
        if (learner == null)
        {
            learner = _dictionaries.NewRecord<Learner>();
            learner.Email = email;
            learner.Name = email;
            learner = await _dictionaries.SaveRecordAsync(learner);
        }

        await OpenAsync(learner.MetaId, unit.PublishedRevision);
        await EnrollAsync(learner.MetaId, unit.PublishedRevision);
        return unit.PublishedRevision;
    }

    private async Task EnrollAsync(Guid learnerId, Guid unitRevisionId)
    {
        var page = await _dictionaries.GetRecordAsync<UnitRevision>(unitRevisionId);
        if (page == null || page.ModuleRevision == Guid.Empty) return;

        var links = await _links.GetRecordsAsync<LT_TrackRevisionModule>(
            new Dictionary<string, object?> { ["ModuleRevision"] = page.ModuleRevision });
        foreach (var link in links)
        {
            if (link.TrackRevision is not Guid trackRevisionId || trackRevisionId == Guid.Empty) continue;
            var revision = await _dictionaries.GetRecordAsync<TrackRevision>(trackRevisionId);
            if (revision == null || revision.Track == Guid.Empty) continue;

            var existing = await _info.SliceLastAsync(
                "Enrollment",
                DateTime.UtcNow.AddMinutes(1),
                new Dictionary<string, object?>
                {
                    ["Learner"] = learnerId,
                    ["Track"] = revision.Track,
                });
            if (existing.Count > 0) continue;

            await _info.SetAsync(
                "Enrollment",
                DateTime.UtcNow,
                new Dictionary<string, object?>
                {
                    ["Learner"] = learnerId,
                    ["Track"] = revision.Track,
                },
                new Dictionary<string, object?>
                {
                    // Число, не enum: Npgsql не пишет сгенерированный тип.
                    ["Source"] = (int)EnrollmentSource.Self,
                });
        }
    }

    private static Guid AsGuid(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value == null) return Guid.Empty;
        if (value is Guid id) return id;
        return Guid.TryParse(value.ToString(), out var parsed) ? parsed : Guid.Empty;
    }

    private static string Esc(string value) => (value ?? "").Replace("'", "''");

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
                // Число, не enum: Npgsql не пишет сгенерированный тип.
                ["Kind"] = (int)LearningEventKind.Opened,
            });
    }
}
