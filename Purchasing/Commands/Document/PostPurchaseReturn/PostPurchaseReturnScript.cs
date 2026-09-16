using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class PostPurchaseReturnCommand
{
    public override async Task ExecuteAsync(PurchaseReturn document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<PurchaseReturn>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустой возврат: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("Количество в строке должно быть больше нуля."));
            return;
        }

        var stock = context.GetService<IStockAvailabilityService>();
        foreach (var group in full.Lines.GroupBy(l => l.Item))
        {
            var qty = group.Sum(l => l.Quantity);
            if (await stock.HasSufficientStockAsync(full.Location, group.Key, qty)) continue;
            var onHand = await stock.OnHandAsync(full.Location, group.Key);
            context.AddClientAction(ClientAction.Message(
                $"Не хватает остатка в ячейке: нужно {qty}, есть {onHand}."));
            return;
        }

        full.Subtype = PurchaseReturn.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Возврат поставщику проведён."));
    }
}
