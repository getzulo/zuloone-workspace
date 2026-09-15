using System.Linq;
using ZuloOne.Managers;

// "Complete trip": the same stop checks as dispatch. ISalesFulfillmentService
// .CompleteTripAsync is called from OnAfterPost — do not call it from here or invoices double.
public partial class CompleteTripCommand
{
    public override async Task ExecuteAsync(DeliveryTrip document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<DeliveryTrip>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя завершить пустой рейс: добавьте точки."));
            return;
        }
        if (full.Lines.Any(l => l.SalesOrder == Guid.Empty))
        {
            context.AddClientAction(ClientAction.Message("У каждой точки должен быть заказ."));
            return;
        }

        full.Subtype = DeliveryTrip.Subtypes.Completed;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Рейс завершён."));
    }
}
