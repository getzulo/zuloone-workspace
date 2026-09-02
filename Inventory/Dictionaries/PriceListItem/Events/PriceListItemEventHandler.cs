#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;

namespace ZuloOne.Runtime.Generated;

// Проверки строки прайс-листа.
//
// Тройка (прайс, товар, единица) сама по себе НЕ уникальна: у одной и той же
// цены бывает история — с января по март одна, с апреля другая. Уникальность
// здесь во времени: два интервала одной тройки не имеют права пересекаться.
//
// Почему пересечение отклоняется, а не разрешается «взять последнюю»: подбор
// цены обязан быть однозначным. Разрешив пересечение, мы получили бы цену,
// зависящую от того, какая строка попалась первой, и расхождение вылезло бы не
// здесь, а в сумме проведённого документа. Ровно то же правило и по той же
// причине действует у налоговых ставок (TaxService).
public partial class PriceListItemEventHandler : TypedDictionaryEventHandler<PriceListItem>
{
    public override async Task<EventResult> OnBeforeSaveAsync(PriceListItem record, bool isNew, EventContext context)
    {
        if (record.Price <= 0m)
            return EventResult.Cancel("Цена должна быть больше нуля");

        // Пустая граница — открытый конец интервала, а не «сегодня»: цена без
        // EffectiveFrom действует с начала времён, без EffectiveTo — бессрочно.
        var from = record.EffectiveFrom ?? DateTime.MinValue;
        var to = record.EffectiveTo ?? DateTime.MaxValue;
        if (from > to)
            return EventResult.Cancel("Дата начала действия цены позже даты окончания");

        var item = await context.GetService<IDictionaryManager<Item>>().GetRecordAsync(record.Item);
        if (item == null)
            return EventResult.Cancel("Товар не найден");

        // Цена задаётся либо за базовую единицу товара, либо за его упаковку.
        // Единица чужого вида величины (цена за килограмм у штучного товара)
        // непересчитываема — и это надо поймать здесь, а не молча вернуть null
        // при подборе цены.
        if (record.Unit != item.UnitOfMeasure)
        {
            var factor = await context.GetService<IUnitConverter>().FactorAsync(record.Unit, item.UnitOfMeasure);
            var packaging = (await context.GetService<IDictionaryManager<ItemUnit>>()
                    .GetRecordsAsync($"Item = '{record.Item}' AND Unit = '{record.Unit}'"))
                .Any();
            if (factor == null && !packaging)
                return EventResult.Cancel(
                    "Цену нельзя задать в этой единице: она не базовая для товара, не заведена его упаковкой "
                    + "и не приводится к базовой по виду величины");
        }

        var siblings = (await context.GetService<IDictionaryManager<PriceListItem>>()
                .GetRecordsAsync($"PriceList = '{record.PriceList}' AND Item = '{record.Item}' AND Unit = '{record.Unit}'"))
            .Where(r => r.MetaId != record.MetaId);

        foreach (var other in siblings)
        {
            var otherFrom = other.EffectiveFrom ?? DateTime.MinValue;
            var otherTo = other.EffectiveTo ?? DateTime.MaxValue;
            // Интервалы включительны с обеих сторон, поэтому касание границами —
            // тоже пересечение: 31 марта и «с 31 марта» дали бы две цены на день.
            if (from <= otherTo && otherFrom <= to)
                return EventResult.Cancel(
                    "Для этого товара в этом прайс-листе и этой единице уже есть цена на пересекающийся период");
        }

        return EventResult.Ok();
    }
}
