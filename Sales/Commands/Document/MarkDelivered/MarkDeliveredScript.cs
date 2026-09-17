using System.Linq;
using ZuloOne.Managers;

// «Отметить доставленным»: строки заказа на месте. InvoiceOrderAsync —
// OnAfterPost доставки, отсюда не звать (второй счёт).
public partial class MarkDeliveredCommand
{
    public override async Task ExecuteAsync(SalesOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesOrder>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя доставить пустой заказ: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("В каждой строке количество должно быть больше нуля."));
            return;
        }

        full.Subtype = SalesOrder.Subtypes.Delivered;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заказ доставлен."));
    }
}
