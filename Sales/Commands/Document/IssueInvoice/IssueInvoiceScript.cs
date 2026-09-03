using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Выставить счёт»: ячейка ОТБОРА, остаток в базовой единице, ставка налога
// на дату счёта. CreateCalculationAsync и штамп юрлица — OnBefore/AfterPost.
public partial class IssueInvoiceCommand
{
    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesInvoice>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя выставить пустой счёт: добавьте строки."));
            return;
        }

        if (!await context.GetService<IStoreCellService>()
                .IsCellAllowedForAsync(full.Location, StoreCellPurpose.Picking))
        {
            context.AddClientAction(ClientAction.Message(
                "Отгрузка идёт из ячейки ОТБОРА — у выбранной ячейки другое назначение."));
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
                context.AddClientAction(ClientAction.Message("Количество в строке должно быть больше нуля."));
                return;
            }
            demand[line.Item] = (demand.TryGetValue(line.Item, out var d) ? d : 0m) + qty;
        }

        foreach (var kv in demand)
        {
            if (await stock.HasSufficientStockAsync(full.Location, kv.Key, kv.Value)) continue;
            var onHand = await stock.OnHandAsync(full.Location, kv.Key);
            context.AddClientAction(ClientAction.Message(
                $"Недостаточно остатка на ячейке: требуется {kv.Value}, в наличии {onHand}"));
            return;
        }

        var tax = context.GetService<ITaxService>();
        var taxCode = await tax.ResolveDefaultTaxCodeAsync();
        if (taxCode is not null)
        {
            var taxPoint = full.DocumentDate == default ? DateTime.UtcNow.Date : full.DocumentDate.Date;
            if (await tax.ResolveRateAsync(taxCode.Value, taxPoint) is null)
            {
                context.AddClientAction(ClientAction.Message(
                    $"Налоговый код настроен, но действующей ставки на {taxPoint:yyyy-MM-dd} нет — счёт не выставляется."));
                return;
            }
        }

        full.Subtype = SalesInvoice.Subtypes.Issued;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Счёт выставлен."));
    }

    static async Task<decimal> BaseQtyAsync(
        IItemQuantityConverter conv, Guid item, decimal qty, decimal baseQty, Guid unit)
    {
        if (baseQty != 0m) return baseQty;
        if (unit == Guid.Empty || item == Guid.Empty) return qty;
        return await conv.ToBaseAsync(item, qty, unit) ?? qty;
    }
}
