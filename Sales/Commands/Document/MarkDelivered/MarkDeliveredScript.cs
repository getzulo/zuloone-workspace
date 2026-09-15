using System.Linq;
using ZuloOne.Managers;

// "Mark delivered": order lines must be present. InvoiceOrderAsync runs in
// OnAfterPost of delivery — do not call it from here (a second invoice).
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
