using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Провести расход»: спрос в базовой единице (конвертер) против остатка ячейки
// (IStockAvailabilityService). Регистр Stock допускает минус — движок сам не
// остановит сверхлимит.
public partial class PostGoodsIssueCommand
{
    public override async Task ExecuteAsync(GoodsIssue document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<GoodsIssue>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустой расход: добавьте строки."));
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
                context.AddClientAction(ClientAction.Message("Количество отпуска должно быть больше нуля."));
                return;
            }
            demand[line.Item] = (demand.TryGetValue(line.Item, out var d) ? d : 0m) + qty;
        }

        foreach (var kv in demand)
        {
            if (await stock.HasSufficientStockAsync(full.FromCell, kv.Key, kv.Value)) continue;
            var onHand = await stock.OnHandAsync(full.FromCell, kv.Key);
            context.AddClientAction(ClientAction.Message(
                $"Отгрузка сверх остатка: отгружается {kv.Value}, в наличии {onHand}"));
            return;
        }

        full.Subtype = GoodsIssue.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Расход проведён."));
    }

    static async Task<decimal> BaseQtyAsync(
        IItemQuantityConverter conv, Guid item, decimal qty, decimal baseQty, Guid unit)
    {
        if (baseQty != 0m) return baseQty;
        if (unit == Guid.Empty || item == Guid.Empty) return qty;
        return await conv.ToBaseAsync(item, qty, unit) ?? qty;
    }
}
