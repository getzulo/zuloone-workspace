#nullable enable
using System.Linq;
using ZuloOne.Core.Services;

namespace ZuloOne.Runtime.Generated;

// Проверки прайс-листа. Строки с ценами проверяет PriceListItemEventHandler —
// здесь только сам заголовок.
public partial class PriceListEventHandler : TypedDictionaryEventHandler<PriceList>
{
    public override async Task<EventResult> OnBeforeSaveAsync(PriceList record, bool isNew, EventContext context)
    {
        // Имя — то, чем прайс выбирают в карточке клиента, поэтому оно должно
        // быть различимым. Уникальность проверяется здесь, а не индексом: имя
        // редактируемое, и внятный отказ полезнее ошибки СУБД.
        var duplicate = (await context.GetService<IDictionaryManager<PriceList>>()
                .GetRecordsAsync($"Name = '{record.Name?.Replace("'", "''")}'"))
            .FirstOrDefault(r => r.MetaId != record.MetaId);
        if (duplicate != null)
            return EventResult.Cancel("Прайс-лист с таким наименованием уже есть");

        return EventResult.Ok();
    }
}
