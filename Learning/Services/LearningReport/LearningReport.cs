#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Отчёт стенда видит только владелец этой организации.
// Участник и посторонний получают отказ без чужих адресов.
public partial class LearningReport
{
    private readonly IDictionaryManager _dictionaries;
    private readonly IDocumentManager _documents;
    private readonly IInformationRegisterService _info;
    private readonly ILinkTableManager _links;

    public LearningReport(
        IDictionaryManager dictionaries,
        IDocumentManager documents,
        IInformationRegisterService info,
        ILinkTableManager links)
    {
        _dictionaries = dictionaries;
        _documents = documents;
        _info = info;
        _links = links;
    }

    /// <summary>
    /// Closed paths, modules and certificate numbers of one stand.
    /// A caller who is not an Owner of that stand gets a refusal with no addresses.
    /// </summary>
    public async Task<string> ForStandAsync(string email, string standSlug)
    {
        const string denied = "{\"error\":\"Нет доступа\"}";
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(standSlug))
            return denied;

        var org = (await _dictionaries.GetRecordsAsync<Organization>($"StandSlug = '{Esc(standSlug)}'")).FirstOrDefault();
        var caller = (await _dictionaries.GetRecordsAsync<Learner>($"Email = '{Esc(email)}'")).FirstOrDefault();
        if (org == null || caller == null) return denied;

        var rows = await _links.GetAsync("Membership", new Dictionary<string, object?> { ["Organization"] = org.MetaId });
        var isOwner = rows.Any(row => GuidOf(row, "Learner") == caller.MetaId && RoleOf(row) == "Owner");
        if (!isOwner) return denied;

        var seen = new HashSet<Guid>();
        var people = new List<string>();
        foreach (var row in rows.OrderBy(item => Text(item, "Learner")))
        {
            var learnerId = GuidOf(row, "Learner");
            if (learnerId == Guid.Empty || !seen.Add(learnerId)) continue;
            var learner = await _dictionaries.GetRecordAsync<Learner>(learnerId);
            if (learner == null || string.IsNullOrWhiteSpace(learner.Email)) continue;
            people.Add(await PersonAsync(learner));
        }
        return "{\"people\":[" + string.Join(",", people) + "]}";
    }

    /// <summary>
    /// An owner puts a member of this stand on a path. No message is sent.
    /// A member cannot assign, and the refusal names nobody else.
    /// </summary>
    public async Task<string> AssignAsync(string ownerEmail, string standSlug, string requestJson)
    {
        const string denied = "{\"error\":\"Нет доступа\"}";
        if (string.IsNullOrWhiteSpace(ownerEmail) || string.IsNullOrWhiteSpace(standSlug))
            return denied;

        var org = (await _dictionaries.GetRecordsAsync<Organization>($"StandSlug = '{Esc(standSlug)}'")).FirstOrDefault();
        var owner = (await _dictionaries.GetRecordsAsync<Learner>($"Email = '{Esc(ownerEmail)}'")).FirstOrDefault();
        if (org == null || owner == null) return denied;

        var rows = await _links.GetAsync("Membership", new Dictionary<string, object?> { ["Organization"] = org.MetaId });
        var isOwner = rows.Any(row => GuidOf(row, "Learner") == owner.MetaId && RoleOf(row) == "Owner");
        if (!isOwner) return denied;

        var learnerEmail = Field(requestJson, "learner");
        var trackId = Field(requestJson, "track");
        var learner = (await _dictionaries.GetRecordsAsync<Learner>($"Email = '{Esc(learnerEmail)}'")).FirstOrDefault();
        var member = learner != null && rows.Any(row => GuidOf(row, "Learner") == learner.MetaId);
        if (!member) return "{\"error\":\"Нет такого участника\"}";

        var track = (await _dictionaries.GetRecordsAsync<Track>($"StableId = '{Esc(trackId)}'")).FirstOrDefault();
        if (track == null) return "{\"error\":\"Путь не опубликован\"}";

        var resources = new Dictionary<string, object?>
        {
            // Число, не enum: Npgsql не пишет сгенерированный тип.
            ["Source"] = (int)EnrollmentSource.Assigned,
        };
        if (DateTime.TryParse(Field(requestJson, "due"), out var due))
            resources["DueAt"] = due.Date;

        await _info.SetAsync(
            "Enrollment",
            DateTime.UtcNow,
            new Dictionary<string, object?>
            {
                ["Learner"] = learner!.MetaId,
                ["Track"] = track.MetaId,
            },
            resources);
        return "{\"ok\":true}";
    }

    private static string Field(string? json, string name)
    {
        var marker = "\"" + name + "\":\"";
        var text = json ?? "";
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return "";
        at += marker.Length;
        var end = text.IndexOf('"', at);
        return end < 0 ? "" : text.Substring(at, end - at);
    }

    private async Task<string> PersonAsync(Learner learner)
    {
        var tracks = await ClosedTracksAsync(learner.MetaId);
        var open = await OpenTracksAsync(learner.MetaId, tracks);
        var modules = await ClosedModulesAsync(learner.MetaId);
        var certificates = await _documents.QueryDocumentsAsync<Certificate>($"Learner = '{learner.MetaId}'");
        var numbers = certificates
            .Select(item => item.ID)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct()
            .OrderBy(item => item);
        return "{\"email\":" + Quote(learner.Email)
            + ",\"tracks\":[" + string.Join(",", tracks.Select(Quote)) + "]"
            + ",\"modules\":[" + string.Join(",", modules.Select(Quote)) + "]"
            + ",\"certificates\":[" + string.Join(",", numbers.Select(Quote)) + "]"
            + ",\"open\":[" + string.Join(",", open.Select(Quote)) + "]}";
    }

    private async Task<List<string>> ClosedTracksAsync(Guid learnerId)
    {
        var cards = await _dictionaries.GetRecordsAsync<Track>("1 = 1");
        var awards = await _info.SliceLastAsync(
            "TrackAward",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?> { ["Learner"] = learnerId });
        var names = new List<string>();
        foreach (var award in awards)
        {
            if (!AsBool(award, "Awarded")) continue;
            var revision = GuidOf(award, "TrackRevision");
            var name = cards.FirstOrDefault(card => card.PublishedRevision == revision)?.Name;
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                names.Add(name);
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private async Task<List<string>> OpenTracksAsync(Guid learnerId, List<string> closed)
    {
        var cards = await _dictionaries.GetRecordsAsync<Track>("1 = 1");
        var rows = await _info.SliceLastAsync(
            "Enrollment",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?> { ["Learner"] = learnerId });
        var names = new List<string>();
        foreach (var row in rows)
        {
            if (!IsAssigned(row)) continue;
            var name = cards.FirstOrDefault(card => card.MetaId == GuidOf(row, "Track"))?.Name;
            if (!string.IsNullOrWhiteSpace(name) && !closed.Contains(name) && !names.Contains(name))
                names.Add(name);
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static bool IsAssigned(IDictionary<string, object?> row)
    {
        if (!row.TryGetValue("Source", out var value) || value == null) return false;
        if (value is int number) return number == (int)EnrollmentSource.Assigned;
        var text = value.ToString() ?? "";
        return text == "2" || text == "Assigned";
    }

    private async Task<List<string>> ClosedModulesAsync(Guid learnerId)
    {
        var cards = await _dictionaries.GetRecordsAsync<Module>("1 = 1");
        var awards = await _info.SliceLastAsync(
            "ModuleAward",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?> { ["Learner"] = learnerId });
        var names = new List<string>();
        foreach (var award in awards)
        {
            if (!AsBool(award, "Awarded")) continue;
            var revision = GuidOf(award, "ModuleRevision");
            var name = cards.FirstOrDefault(card => card.PublishedRevision == revision)?.Name;
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                names.Add(name);
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static string RoleOf(IDictionary<string, object?> row)
        => Text(row, "Role");

    private static string Text(IDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value == null) return "";
        return value.ToString() ?? "";
    }

    private static Guid GuidOf(IDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value == null) return Guid.Empty;
        if (value is Guid id) return id;
        return Guid.TryParse(value.ToString(), out var parsed) ? parsed : Guid.Empty;
    }

    private static bool AsBool(IDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value == null) return false;
        if (value is bool flag) return flag;
        var text = value.ToString();
        return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1";
    }

    private static string Quote(string? value)
        => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Esc(string value) => (value ?? "").Replace("'", "''");
}
