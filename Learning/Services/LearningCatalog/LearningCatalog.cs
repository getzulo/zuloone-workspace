#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Карточка одна на StableId. Совпадение текста с опубликованной версией
// ничего не пишет. Другой текст добавляет версию и переставляет указатель.
public partial class LearningCatalog
{
    private readonly IDictionaryManager _dictionaries;

    public LearningCatalog(IDictionaryManager dictionaries) => _dictionaries = dictionaries;

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
    public async Task<Guid> PublishUnitAsync(
        string stableId, Guid moduleRevisionId, string name, string body, int sortOrder)
    {
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
            && string.Equals(current.Body, body, StringComparison.Ordinal)
            && current.SortOrder == sortOrder
            && current.ModuleRevision == moduleRevisionId)
            return current.MetaId;

        var revision = _dictionaries.NewRecord<UnitRevision>();
        revision.Unit = unit.MetaId;
        revision.ModuleRevision = moduleRevisionId;
        revision.Number = current == null ? 1 : current.Number + 1;
        revision.Name = name;
        revision.Body = body ?? "";
        revision.Kind = UnitKind.Lesson;
        revision.SortOrder = sortOrder;
        revision.PublishedAt = DateTime.UtcNow;
        revision = await _dictionaries.SaveRecordAsync(revision);

        unit.Name = name;
        unit.PublishedRevision = revision.MetaId;
        await _dictionaries.SaveRecordAsync(unit);
        return revision.MetaId;
    }

    private async Task<T?> FindAsync<T>(string filter) where T : class, new()
    {
        var rows = await _dictionaries.GetRecordsAsync<T>(filter);
        return rows.FirstOrDefault();
    }

    private static string Esc(string value) => (value ?? "").Replace("'", "''");
}
