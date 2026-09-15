using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Провести перемещение»: остаток исходной ячейки в базовой единице.
public partial class PostStockTransferCommand
{
    public override async Task ExecuteAsync(StockTransfer document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<StockTransfer>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустое перемещение: добавьте строки."));
            return;
        }

        var stock = context.GetService<IStockAvailabilityService>();
        var conv = context.GetService<IItemQuantityConverter>();
        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in full.Lines)
        {
            var qty = await BaseQtyAsync(conv, line.Item, line.Quantity, line.BaseQuantity, line.Unit);
            if (qty <= 0m)
            {
                context.AddClientAction(ClientAction.Message("Количество перемещения должно быть больше нуля."));
                return;
            }
            demand[line.Item] = (demand.TryGetValue(line.Item, out var d) ? d : 0m) + qty;
        }

        foreach (var kv in demand)
        {
            if (await stock.HasSufficientStockAsync(full.FromCell, kv.Key, kv.Value)) continue;
            var onHand = await stock.OnHandAsync(full.FromCell, kv.Key);
            context.AddClientAction(ClientAction.Message(
                $"Перемещение сверх остатка: перемещается {kv.Value}, в наличии {onHand}"));
            return;
        }

        full.Subtype = StockTransfer.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Перемещение проведено."));
    }

    static async Task<decimal> BaseQtyAsync(
        IItemQuantityConverter conv, Guid item, decimal qty, decimal baseQty, Guid unit)
    {
        if (baseQty != 0m) return baseQty;
        if (unit == Guid.Empty || item == Guid.Empty) return qty;
        return await conv.ToBaseAsync(item, qty, unit) ?? qty;
    }
}
