#nullable enable
using System.Linq;
using ZuloOne.Core.Services;

namespace ZuloOne.Runtime.Generated;

// Проверки типа цены. Строки с ценами проверяет PriceListItemEventHandler —
// здесь только сам заголовок.
public partial class PriceListEventHandler : TypedDictionaryEventHandler<PriceList>
{
    private const int MaxPriceTypeChainDepth = 20;

    public override async Task<EventResult> OnBeforeSaveAsync(PriceList record, bool isNew, EventContext context)
    {
        // Имя — то, чем тип цены выбирают в карточке клиента, поэтому оно
        // должно быть различимым. Уникальность проверяется здесь, а не
        // индексом: имя редактируемое, и внятный отказ полезнее ошибки СУБД.
        var manager = context.GetService<IDictionaryManager<PriceList>>();
        var duplicate = (await manager
                .GetRecordsAsync($"Name = '{record.Name?.Replace("'", "''")}'"))
            .FirstOrDefault(r => r.MetaId != record.MetaId);
        if (duplicate != null)
            return EventResult.Cancel("Тип цены с таким наименованием уже есть");

        if (record.Kind == PriceListKind.Base)
        {
            if (record.BasePriceType != Guid.Empty || record.MarkupPercent != 0)
                return EventResult.Cancel(
                    "Базовый тип цены не может ссылаться на другой тип цены и не может иметь наценку — заполни цены строками (Price type rows)");
            return EventResult.Ok();
        }

        // Kind == Calculated
        if (record.BasePriceType == Guid.Empty)
            return EventResult.Cancel("Динамический тип цены обязан ссылаться на базовый тип цены (Base price type)");

        // MetaId у записи присвоен конструктором сущности ДО первого
        // сохранения (MetadataRecordBase), поэтому сид визитед-сета self-ом
        // безопасен и для isNew — ловит и прямую само-ссылку, и транзитивный цикл.
        var visited = new HashSet<Guid> { record.MetaId };
        var currentId = record.BasePriceType;
        var depth = 0;
        while (true)
        {
            if (!visited.Add(currentId))
                return EventResult.Cancel("Цепочка базовых типов цены зациклилась");

            if (++depth > MaxPriceTypeChainDepth)
                return EventResult.Cancel($"Цепочка базовых типов цены длиннее {MaxPriceTypeChainDepth} уровней");

            var current = await manager.GetRecordAsync(currentId);
            if (current == null || current.Kind == PriceListKind.Base || current.BasePriceType == Guid.Empty)
                break;

            currentId = current.BasePriceType;
        }

        return EventResult.Ok();
    }
}
