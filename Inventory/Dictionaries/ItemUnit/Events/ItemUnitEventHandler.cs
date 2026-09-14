#nullable enable
using System.Linq;
using ZuloOne.Core.Services;

namespace ZuloOne.Runtime.Generated;

// Item packaging checks. The previous conversion model had none at all —
// that is why the stand grew contradictory rules (tonne→gram set up
// separately from tonne→kilogram and kilogram→gram, and they only matched by chance).
public partial class ItemUnitEventHandler : TypedDictionaryEventHandler<ItemUnit>
{
    public override async Task<EventResult> OnBeforeSaveAsync(ItemUnit record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.QtyInBaseUnit <= 0m)
            return EventResult.Cancel("Количество в базовой единице должно быть больше нуля");

        var item = await context.GetService<IDictionaryManager<Item>>().GetRecordAsync(record.Item);
        if (item == null)
            return EventResult.Cancel("Товар не найден");

        // The item's base unit is the unit IN WHICH packaging is counted;
        // packaging "into itself" would mean a factor of 1 and only confuse.
        if (record.Unit == item.UnitOfMeasure)
            return EventResult.Cancel(
                "Упаковка не может совпадать с базовой единицей товара — её коэффициент по определению равен 1");

        // The (item, unit) pair is unique: two packagings of the same item in the
        // same unit are two different answers to one question, and conversion
        // would depend on which row came first.
        var duplicate = (await context.GetService<IDictionaryManager<ItemUnit>>()
                .GetRecordsAsync($"Item = '{record.Item}' AND Unit = '{record.Unit}'"))
            .FirstOrDefault(r => r.MetaId != record.MetaId);
        if (duplicate != null)
            return EventResult.Cancel("Для этого товара упаковка в этой единице уже заведена");

        return EventResult.Ok();
    }
}
