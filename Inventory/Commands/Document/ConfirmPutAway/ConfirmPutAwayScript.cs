using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Подтвердить приёмку»: дисциплина ячеек (приёмка → хранение) и остаток
// в ячейке приёмки — через IStoreCellService / IStockAvailabilityService.
// Количество в регистр идёт в базовой единице: IItemQuantityConverter, если
// платформа ещё не заполнила BaseQuantity.
public partial class ConfirmPutAwayCommand
{
    public override async Task ExecuteAsync(PutAwayTask document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<PutAwayTask>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя подтвердить пустую приёмку: добавьте строки."));
            return;
        }

        var cells = context.GetService<IStoreCellService>();
        var stock = context.GetService<IStockAvailabilityService>();
        var conv = context.GetService<IItemQuantityConverter>();
        var enforcing = await cells.IsWarehouseDisciplineOnAsync();

        if (enforcing && !await cells.IsCellAllowedForAsync(full.FromCell, StoreCellPurpose.Receiving))
        {
            context.AddClientAction(ClientAction.Message(
                "Раскладка забирает товар из ячейки ПРИЁМКИ — у выбранной ячейки другое назначение."));
            return;
        }

        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in full.Lines)
        {
            if (enforcing && !await cells.IsCellAllowedForAsync(line.ToCell, StoreCellPurpose.Storage))
            {
                context.AddClientAction(ClientAction.Message(
                    "Раскладка кладёт товар в ячейку ХРАНЕНИЯ — у выбранной ячейки другое назначение."));
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
                $"Недостаточно товара в ячейке приёмки: требуется {kv.Value}, в наличии {onHand}"));
            return;
        }

        full.Subtype = PutAwayTask.Subtypes.Confirmed;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Приёмка подтверждена."));
    }

    static async Task<decimal> BaseQtyAsync(
        IItemQuantityConverter conv, Guid item, decimal qty, decimal baseQty, Guid unit)
    {
        if (baseQty != 0m) return baseQty;
        if (unit == Guid.Empty || item == Guid.Empty) return qty;
        return await conv.ToBaseAsync(item, qty, unit) ?? qty;
    }
}
