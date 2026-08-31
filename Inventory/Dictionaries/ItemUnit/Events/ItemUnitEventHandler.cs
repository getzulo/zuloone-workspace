#nullable enable
using System.Linq;
using ZuloOne.Core.Services;

namespace ZuloOne.Runtime.Generated;

// Проверки упаковки товара. Их не было в прежней модели пересчёта вообще —
// поэтому на стенде и появились противоречивые правила (тонна→грамм заведена
// отдельно от тонна→килограмм и килограмм→грамм, и согласованы они случайно).
public partial class ItemUnitEventHandler : TypedDictionaryEventHandler<ItemUnit>
{
    public override async Task<EventResult> OnBeforeSaveAsync(ItemUnit record, bool isNew, EventContext context)
    {
        if (record.QtyInBaseUnit <= 0m)
            return EventResult.Cancel("Количество в базовой единице должно быть больше нуля");

        var item = await context.GetService<IDictionaryManager<Item>>().GetRecordAsync(record.Item);
        if (item == null)
            return EventResult.Cancel("Товар не найден");

        // Базовая единица товара — это единица, В КОТОРОЙ считается упаковка;
        // упаковка «сама в себя» означала бы коэффициент 1 и только путала.
        if (record.Unit == item.UnitOfMeasure)
            return EventResult.Cancel(
                "Упаковка не может совпадать с базовой единицей товара — её коэффициент по определению равен 1");

        // Пара (товар, единица) уникальна: две упаковки одного товара в одной
        // единице — это два разных ответа на один вопрос, и пересчёт стал бы
        // зависеть от того, какая строка попалась первой.
        var duplicate = (await context.GetService<IDictionaryManager<ItemUnit>>()
                .GetRecordsAsync($"Item = '{record.Item}' AND Unit = '{record.Unit}'"))
            .FirstOrDefault(r => r.MetaId != record.MetaId);
        if (duplicate != null)
            return EventResult.Cancel("Для этого товара упаковка в этой единице уже заведена");

        return EventResult.Ok();
    }
}
