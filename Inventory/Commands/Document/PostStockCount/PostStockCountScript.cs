using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Провести инвентаризацию»: факт в базовой единице через конвертер.
// Дельту (факт − система) пишет OnBeforePost через IDataService — команда
// только читает OnHand, чтобы отказать на пустых/нулевых строках до перехода.
public partial class PostStockCountCommand
{
    public override async Task ExecuteAsync(StockCount document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<StockCount>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустую инвентаризацию: добавьте строки."));
            return;
        }

        var conv = context.GetService<IItemQuantityConverter>();
        foreach (var line in full.Lines)
        {
            if (line.Item == Guid.Empty)
            {
                context.AddClientAction(ClientAction.Message("В каждой строке должен быть товар."));
                return;
            }

            var counted = await BaseQtyAsync(conv, line.Item, line.CountedQty, line.BaseQuantity, line.Unit);
            if (counted < 0m)
            {
                context.AddClientAction(ClientAction.Message("Фактическое количество не может быть отрицательным."));
                return;
            }
        }

        full.Subtype = StockCount.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Инвентаризация проведена."));
    }

    static async Task<decimal> BaseQtyAsync(
        IItemQuantityConverter conv, Guid item, decimal qty, decimal baseQty, Guid unit)
    {
        if (baseQty != 0m) return baseQty;
        if (unit == Guid.Empty || item == Guid.Empty) return qty;
        return await conv.ToBaseAsync(item, qty, unit) ?? qty;
    }
}
