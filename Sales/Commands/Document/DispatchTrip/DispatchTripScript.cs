using System.Linq;
using ZuloOne.Managers;

// "Dispatch trip": every stop must have an order. CompleteTripAsync runs in
// OnAfterPost of completion, not of dispatch.
public partial class DispatchTripCommand
{
    public override async Task ExecuteAsync(DeliveryTrip document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<DeliveryTrip>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя отправить пустой рейс: добавьте точки."));
            return;
        }
        if (full.Lines.Any(l => l.SalesOrder == Guid.Empty))
        {
            context.AddClientAction(ClientAction.Message("У каждой точки должен быть заказ."));
            return;
        }

        full.Subtype = DeliveryTrip.Subtypes.Dispatched;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Рейс отправлен."));
    }
}
