using System;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

public partial class CompleteTripCommand
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

        // Stamp before the read-only Completed subtype; a later header write is refused.
        full.ActualComplete = DateTime.UtcNow;
        full.Subtype = DeliveryTrip.Subtypes.Completed;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Рейс завершён."));
    }
}
