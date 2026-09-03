using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Подтвердить отбор»: ячейки (хранение → отбор) и остаток в базовой единице
// спрашиваются у сервисов, а не считаются здесь. OnAfterPost не зовём — проводок
// нет, только смена подтипа; события повторят те же проверки на любом пути.
public partial class ConfirmPickCommand
{
    public override async Task ExecuteAsync(PickTask document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<PickTask>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя подтвердить пустой отбор: добавьте строки."));
            return;
        }

        var cells = context.GetService<IStoreCellService>();
        var stock = context.GetService<IStockAvailabilityService>();
        var conv = context.GetService<IItemQuantityConverter>();
        var enforcing = await cells.IsWarehouseDisciplineOnAsync();

        if (enforcing && !await cells.IsCellAllowedForAsync(full.FromCell, StoreCellPurpose.Storage))
        {
            context.AddClientAction(ClientAction.Message(
                "Отбор забирает товар из ячейки ХРАНЕНИЯ — у выбранной ячейки другое назначение."));
            return;
        }

        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in full.Lines)
        {
            if (enforcing && !await cells.IsCellAllowedForAsync(line.ToCell, StoreCellPurpose.Picking))
            {
                context.AddClientAction(ClientAction.Message(
                    "Отбор кладёт товар в ячейку ОТБОРА — у выбранной ячейки другое назначение."));
                return;
            }

            var qty = await BaseQtyAsync(conv, line.Item, line.Quantity, line.BaseQuantity, line.Unit);
            if (qty <= 0m)
            {
                context.AddClientAction(ClientAction.Message("Количество в строке должно быть больше нуля."));
                return;
            }
            demand[line.Item] = (demand.TryGetValue(line.Item, out var d) ? d : 0m) + qty;
        }

        foreach (var kv in demand)
        {
            if (await stock.HasSufficientStockAsync(full.FromCell, kv.Key, kv.Value)) continue;
            var onHand = await stock.OnHandAsync(full.FromCell, kv.Key);
            context.AddClientAction(ClientAction.Message(
                $"Недостаточно товара в ячейке хранения: требуется {kv.Value}, в наличии {onHand}"));
            return;
        }

        full.Subtype = PickTask.Subtypes.Confirmed;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Отбор подтверждён."));
    }

    static async Task<decimal> BaseQtyAsync(
        IItemQuantityConverter conv, Guid item, decimal qty, decimal baseQty, Guid unit)
    {
        if (baseQty != 0m) return baseQty;
        if (unit == Guid.Empty || item == Guid.Empty) return qty;
        return await conv.ToBaseAsync(item, qty, unit) ?? qty;
    }
}
