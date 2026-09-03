using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Провести корректировку»: минусовые строки — списание, их спрос в базовой
// единице сверяется с остатком ячейки. Плюсовые (оприходование) остаток не жрут.
public partial class PostStockAdjustmentCommand
{
    public override async Task ExecuteAsync(StockAdjustment document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<StockAdjustment>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустую корректировку: добавьте строки."));
            return;
        }

        var stock = context.GetService<IStockAvailabilityService>();
        var conv = context.GetService<IItemQuantityConverter>();
        var writeOff = new Dictionary<Guid, decimal>();
        foreach (var line in full.Lines)
        {
            var qty = await BaseQtyAsync(conv, line.Item, line.Quantity, line.BaseQuantity, line.Unit);
            if (qty < 0m)
                writeOff[line.Item] = (writeOff.TryGetValue(line.Item, out var d) ? d : 0m) + (-qty);
        }

        foreach (var kv in writeOff)
        {
            if (await stock.HasSufficientStockAsync(full.Cell, kv.Key, kv.Value)) continue;
            var onHand = await stock.OnHandAsync(full.Cell, kv.Key);
            context.AddClientAction(ClientAction.Message(
                $"Списание сверх остатка: списывается {kv.Value}, в наличии {onHand}"));
            return;
        }

        full.Subtype = StockAdjustment.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Корректировка проведена."));
    }

    static async Task<decimal> BaseQtyAsync(
        IItemQuantityConverter conv, Guid item, decimal qty, decimal baseQty, Guid unit)
    {
        if (baseQty != 0m) return baseQty;
        if (unit == Guid.Empty || item == Guid.Empty) return qty;
        return await conv.ToBaseAsync(item, qty, unit) ?? qty;
    }
}
