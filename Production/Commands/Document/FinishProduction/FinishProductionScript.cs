using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Завершить выпуск»: компоненты должны быть (разворот — команда ExpandBom,
// отсюда BOM не пишем). Спрос комплектующих в базовой единице против остатка
// ячейки выпуска.
public partial class FinishProductionCommand
{
    public override async Task ExecuteAsync(ProductionOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<ProductionOrder>(document.MetaId);
        if (full == null) return;

        if (full.Product == Guid.Empty || full.Quantity <= 0m)
        {
            context.AddClientAction(ClientAction.Message("Укажите изделие и количество больше нуля."));
            return;
        }

        if (full.Components.Count == 0)
        {
            context.AddClientAction(ClientAction.Message(
                "Заполните компоненты: нажмите «Развернуть спецификацию»."));
            return;
        }

        var stock = context.GetService<IStockAvailabilityService>();
        var conv = context.GetService<IItemQuantityConverter>();
        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in full.Components)
        {
            var qty = await BaseQtyAsync(conv, line.Component, line.QtyRequired, line.BaseQuantity, line.Unit);
            if (qty <= 0m)
            {
                context.AddClientAction(ClientAction.Message("Количество компонента должно быть больше нуля."));
                return;
            }
            demand[line.Component] = (demand.TryGetValue(line.Component, out var d) ? d : 0m) + qty;
        }

        foreach (var kv in demand)
        {
            if (await stock.HasSufficientStockAsync(full.Location, kv.Key, kv.Value)) continue;
            var onHand = await stock.OnHandAsync(full.Location, kv.Key);
            context.AddClientAction(ClientAction.Message(
                $"Недостаточно компонента на ячейке: требуется {kv.Value}, в наличии {onHand}"));
            return;
        }

        full.Subtype = ProductionOrder.Subtypes.Finished;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Выпуск завершён."));
    }

    static async Task<decimal> BaseQtyAsync(
        IItemQuantityConverter conv, Guid item, decimal qty, decimal baseQty, Guid unit)
    {
        if (baseQty != 0m) return baseQty;
        if (unit == Guid.Empty || item == Guid.Empty) return qty;
        return await conv.ToBaseAsync(item, qty, unit) ?? qty;
    }
}
