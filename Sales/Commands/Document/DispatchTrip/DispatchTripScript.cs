using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class DispatchTripCommand
{
    public override async Task ExecuteAsync(DeliveryTrip document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<DeliveryTrip>(document.MetaId);
        if (full == null) return;

        var reason = await context.GetService<IDeliveryService>().ValidateTripAsync(full.MetaId);
        if (reason != null)
        {
            context.AddClientAction(ClientAction.Message(reason));
            return;
        }

        full.Subtype = DeliveryTrip.Subtypes.Dispatched;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Рейс отправлен."));
    }
}
