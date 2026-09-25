#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Карточка одна на StableId. Совпадение текста с опубликованной версией
// ничего не пишет. Другой текст добавляет версию и переставляет указатель.
public partial class LearningCatalog
{
    private readonly IDictionaryManager _dictionaries;
    private readonly ILinkTableManager _links;

    public LearningCatalog(IDictionaryManager dictionaries, ILinkTableManager links)
    {
        _dictionaries = dictionaries;
        _links = links;
    }

    /// <summary>Publish a module revision. Returns the revision id, old or new.</summary>
    public async Task<Guid> PublishModuleAsync(
        string stableId, string name, int minutes, string articlePath, string articleRevision)
    {
        var module = await FindAsync<Module>($"StableId = '{Esc(stableId)}'");
        if (module == null)
        {
            module = _dictionaries.NewRecord<Module>();
            module.StableId = stableId;
            module.Name = name;
            module = await _dictionaries.SaveRecordAsync(module);
        }

        ModuleRevision? current = null;
        if (module.PublishedRevision != Guid.Empty)
            current = await _dictionaries.GetRecordAsync<ModuleRevision>(module.PublishedRevision);

        if (current != null
            && string.Equals(current.Name, name, StringComparison.Ordinal)
            && current.Minutes == minutes
            && string.Equals(current.ArticlePath, articlePath ?? "", StringComparison.Ordinal)
            && string.Equals(current.ArticleRevision, articleRevision ?? "", StringComparison.Ordinal))
            return current.MetaId;

        var revision = _dictionaries.NewRecord<ModuleRevision>();
        revision.Module = module.MetaId;
        revision.Number = current == null ? 1 : current.Number + 1;
        revision.Name = name;
        revision.Minutes = minutes;
        revision.ArticlePath = articlePath ?? "";
        revision.ArticleRevision = articleRevision ?? "";
        revision.PublishedAt = DateTime.UtcNow;
        revision = await _dictionaries.SaveRecordAsync(revision);

        module.Name = name;
        module.PublishedRevision = revision.MetaId;
        await _dictionaries.SaveRecordAsync(module);
        return revision.MetaId;
    }

    /// <summary>Publish a page revision under a module revision. Returns the page revision id.</summary>
    public Task<Guid> PublishUnitAsync(
        string stableId, Guid moduleRevisionId, string name, string body, int sortOrder)
        => PublishUnitCoreAsync(stableId, moduleRevisionId, name, body, sortOrder, UnitKind.Lesson, "");

    /// <summary>
    /// Publish a catalog package. The page body must already be without the answer key.
    /// Returns how many pages were published.
    /// </summary>
    public async Task<int> ImportAsync(string packageJson)
    {
        using var doc = JsonDocument.Parse(packageJson);
        var root = doc.RootElement;
        var moduleRevisions = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var count = 0;
        foreach (var module in root.GetProperty("modules").EnumerateArray())
        {
            var stableId = Str(module, "stableId");
            var revisionId = await PublishModuleAsync(
                stableId,
                Str(module, "name"),
                module.TryGetProperty("minutes", out var minutes) ? minutes.GetInt32() : 0,
                Str(module, "articlePath"),
                Str(module, "articleRevision"));
            moduleRevisions[stableId] = revisionId;
            foreach (var unit in module.GetProperty("units").EnumerateArray())
            {
                var checks = unit.TryGetProperty("checks", out var checkNode) ? checkNode.GetRawText() : "";
                await PublishUnitCoreAsync(
                    Str(unit, "stableId"),
                    revisionId,
                    Str(unit, "name"),
                    Str(unit, "body"),
                    unit.TryGetProperty("sortOrder", out var sort) ? sort.GetInt32() : count + 1,
                    KindOf(Str(unit, "kind")),
                    checks);
                count++;
            }
        }

        if (root.TryGetProperty("tracks", out var tracks))
        {
            foreach (var track in tracks.EnumerateArray())
            {
                var moduleIds = new List<Guid>();
                foreach (var moduleId in track.GetProperty("modules").EnumerateArray())
                {
                    var key = moduleId.GetString() ?? "";
                    if (!moduleRevisions.TryGetValue(key, out var revisionId))
                        throw new InvalidOperationException("В пакете нет модуля " + key);
                    moduleIds.Add(revisionId);
                }
                await PublishTrackAsync(Str(track, "stableId"), Str(track, "name"), moduleIds);
            }
        }

        return count;
    }

    /// <summary>Published page as JSON. Questions are included. The correct option is not.</summary>
    public async Task<string> ReadPageAsync(string stableId)
    {
        var unit = await FindAsync<Unit>($"StableId = '{Esc(stableId)}'");
        if (unit == null || unit.PublishedRevision == Guid.Empty) return "";
        var page = await _dictionaries.GetRecordAsync<UnitRevision>(unit.PublishedRevision);
        if (page == null) return "";

        var prompts = (await _dictionaries.GetRecordsAsync<CheckPrompt>($"UnitRevision = '{page.MetaId}'"))
            .OrderBy(row => row.SortOrder)
            .ToList();
        var questions = new List<string>();
        foreach (var prompt in prompts)
        {
            var options = (await _dictionaries.GetRecordsAsync<CheckOption>($"Prompt = '{prompt.MetaId}'"))
                .OrderBy(row => row.SortOrder)
                .Select(option => "{\"id\":" + Json(option.Name) + ",\"text\":" + Json(option.Text) + "}");
            questions.Add("{\"id\":" + Json(prompt.Name) + ",\"text\":" + Json(prompt.Text) + ",\"options\":[" + string.Join(",", options) + "]}");
        }

        var kind = page.Kind == UnitKind.Check ? "check" : "lesson";
        return "{\"stableId\":" + Json(stableId)
            + ",\"name\":" + Json(page.Name)
            + ",\"kind\":" + Json(kind)
            + ",\"body\":" + Json(page.Body)
            + ",\"questions\":[" + string.Join(",", questions) + "]}";
    }

    private async Task<Guid> PublishUnitCoreAsync(
        string stableId, Guid moduleRevisionId, string name, string body, int sortOrder, UnitKind kind, string checksJson)
    {
        var canon = Canon(checksJson);
        var unit = await FindAsync<Unit>($"StableId = '{Esc(stableId)}'");
        if (unit == null)
        {
            unit = _dictionaries.NewRecord<Unit>();
            unit.StableId = stableId;
            unit.Name = name;
            unit = await _dictionaries.SaveRecordAsync(unit);
        }

        UnitRevision? current = null;
        if (unit.PublishedRevision != Guid.Empty)
            current = await _dictionaries.GetRecordAsync<UnitRevision>(unit.PublishedRevision);

        if (current != null
            && string.Equals(current.Name, name, StringComparison.Ordinal)
            && string.Equals(current.Body, body ?? "", StringComparison.Ordinal)
            && current.SortOrder == sortOrder
            && current.ModuleRevision == moduleRevisionId
            && current.Kind == kind
            && string.Equals(await CanonOfAsync(current.MetaId), canon, StringComparison.Ordinal))
            return current.MetaId;

        var revision = _dictionaries.NewRecord<UnitRevision>();
        revision.Unit = unit.MetaId;
        revision.ModuleRevision = moduleRevisionId;
        revision.Number = current == null ? 1 : current.Number + 1;
        revision.Name = name;
        revision.Body = body ?? "";
        revision.Kind = kind;
        revision.SortOrder = sortOrder;
        revision.PublishedAt = DateTime.UtcNow;
        revision = await _dictionaries.SaveRecordAsync(revision);
        await WriteChecksAsync(revision.MetaId, checksJson);

        unit.Name = name;
        unit.PublishedRevision = revision.MetaId;
        await _dictionaries.SaveRecordAsync(unit);
        return revision.MetaId;
    }

    private async Task<Guid> PublishTrackAsync(string stableId, string name, List<Guid> moduleRevisionIds)
    {
        var track = await FindAsync<Track>($"StableId = '{Esc(stableId)}'");
        if (track == null)
        {
            track = _dictionaries.NewRecord<Track>();
            track.StableId = stableId;
            track.Name = name;
            track = await _dictionaries.SaveRecordAsync(track);
        }

        TrackRevision? current = null;
        if (track.PublishedRevision != Guid.Empty)
            current = await _dictionaries.GetRecordAsync<TrackRevision>(track.PublishedRevision);

        if (current != null
            && string.Equals(current.Name, name, StringComparison.Ordinal)
            && await SameModulesAsync(current.MetaId, moduleRevisionIds))
            return current.MetaId;

        var revision = _dictionaries.NewRecord<TrackRevision>();
        revision.Track = track.MetaId;
        revision.Number = current == null ? 1 : current.Number + 1;
        revision.Name = name;
        revision.PublishedAt = DateTime.UtcNow;
        revision = await _dictionaries.SaveRecordAsync(revision);

        var rows = new List<LT_TrackRevisionModule>();
        for (var i = 0; i < moduleRevisionIds.Count; i++)
        {
            rows.Add(new LT_TrackRevisionModule
            {
                TrackRevision = revision.MetaId,
                ModuleRevision = moduleRevisionIds[i],
                SortOrder = i + 1,
            });
        }
        await _links.ReplaceRecordsAsync("TrackRevision", revision.MetaId, rows);

        track.Name = name;
        track.PublishedRevision = revision.MetaId;
        await _dictionaries.SaveRecordAsync(track);
        return revision.MetaId;
    }

    private async Task<bool> SameModulesAsync(Guid trackRevisionId, List<Guid> moduleRevisionIds)
    {
        var rows = await _links.GetRecordsAsync<LT_TrackRevisionModule>("TrackRevision", trackRevisionId);
        var ordered = rows.OrderBy(row => row.SortOrder ?? 0).Select(row => row.ModuleRevision ?? Guid.Empty).ToList();
        if (ordered.Count != moduleRevisionIds.Count) return false;
        for (var i = 0; i < ordered.Count; i++)
            if (ordered[i] != moduleRevisionIds[i]) return false;
        return true;
    }

    private async Task WriteChecksAsync(Guid unitRevisionId, string checksJson)
    {
        if (string.IsNullOrWhiteSpace(checksJson)) return;
        using var doc = JsonDocument.Parse(checksJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
        var i = 0;
        foreach (var prompt in doc.RootElement.EnumerateArray())
        {
            i++;
            var row = _dictionaries.NewRecord<CheckPrompt>();
            row.UnitRevision = unitRevisionId;
            row.SortOrder = i;
            row.Name = Str(prompt, "id");
            row.Text = Str(prompt, "text");
            row = await _dictionaries.SaveRecordAsync(row);
            if (!prompt.TryGetProperty("options", out var options)) continue;
            var j = 0;
            foreach (var option in options.EnumerateArray())
            {
                j++;
                var opt = _dictionaries.NewRecord<CheckOption>();
                opt.Prompt = row.MetaId;
                opt.SortOrder = j;
                opt.Name = Str(option, "id");
                opt.Text = Str(option, "text");
                opt.IsCorrect = option.TryGetProperty("correct", out var correct)
                    && correct.ValueKind == JsonValueKind.True;
                await _dictionaries.SaveRecordAsync(opt);
            }
        }
    }

    private async Task<string> CanonOfAsync(Guid unitRevisionId)
    {
        var prompts = (await _dictionaries.GetRecordsAsync<CheckPrompt>($"UnitRevision = '{unitRevisionId}'"))
            .OrderBy(row => row.SortOrder).ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToList();
        var lines = new List<string>();
        foreach (var prompt in prompts)
        {
            lines.Add(prompt.SortOrder + "|" + prompt.Name + "|" + prompt.Text);
            var options = (await _dictionaries.GetRecordsAsync<CheckOption>($"Prompt = '{prompt.MetaId}'"))
                .OrderBy(row => row.SortOrder).ThenBy(row => row.Name, StringComparer.Ordinal);
            foreach (var option in options)
                lines.Add(prompt.SortOrder + "." + option.SortOrder + "|" + option.Name + "|" + option.Text + "|" + (option.IsCorrect ? "1" : "0"));
        }
        return string.Join("\n", lines);
    }

    private static string Canon(string checksJson)
    {
        if (string.IsNullOrWhiteSpace(checksJson)) return "";
        using var doc = JsonDocument.Parse(checksJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return "";
        var lines = new List<string>();
        var i = 0;
        foreach (var prompt in doc.RootElement.EnumerateArray())
        {
            i++;
            lines.Add(i + "|" + Str(prompt, "id") + "|" + Str(prompt, "text"));
            if (!prompt.TryGetProperty("options", out var options)) continue;
            var j = 0;
            foreach (var option in options.EnumerateArray())
            {
                j++;
                var correct = option.TryGetProperty("correct", out var mark) && mark.ValueKind == JsonValueKind.True;
                lines.Add(i + "." + j + "|" + Str(option, "id") + "|" + Str(option, "text") + "|" + (correct ? "1" : "0"));
            }
        }
        return string.Join("\n", lines);
    }

    private static UnitKind KindOf(string kind)
        => string.Equals(kind, "check", StringComparison.Ordinal) ? UnitKind.Check : UnitKind.Lesson;

    private static string Str(JsonElement node, string name)
        => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private async Task<T?> FindAsync<T>(string filter) where T : class, new()
    {
        var rows = await _dictionaries.GetRecordsAsync<T>(filter);
        return rows.FirstOrDefault();
    }

    private static string Esc(string value) => (value ?? "").Replace("'", "''");

    private static string Json(string? value)
    {
        var text = value ?? "";
        return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";
    }
}
