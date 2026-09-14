using System.Linq;

// "Place order" command on the purchase-order Draft subtype: a controlled
// Draft → Ordered transition. Previously the order jumped from draft straight
// to receipt, and the "placed with the vendor but not yet arrived" state simply
// did not exist.
//
// Check before the transition: the order must have lines with a positive
// quantity. Fail — message to the user and the document stays put.
public partial class PlaceOrderCommand
{
    public override async Task ExecuteAsync(PurchaseOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();

        // Lines on the command header are empty — the document is re-read.
        var full = await docs.GetDocumentAsync<PurchaseOrder>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя заказать пустой документ: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("В каждой строке количество должно быть больше нуля."));
            return;
        }

        full.Subtype = PurchaseOrder.Subtypes.Ordered;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заказ размещён у поставщика."));
    }
}
