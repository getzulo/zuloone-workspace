using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Release-realization command: Shipped → Issued.
// Stock, legal-entity, and tax-rate checks live in OnBeforePost.
// After the transition: Mix lifts the SalesInvoiceReserveTx reserve and
// writes Stock/Revenue/Receivable/Loyalty/SaudiVat via the Issued scripts.
// The source order is moved to Delivered here (after SaveDocumentAsync),
// not in OnAfterPost — a nested SetSubtypeAsync is silently swallowed there.
public partial class ReleaseRealizationCommand
{
    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesInvoice>(document.MetaId);
        if (full == null) return;

        full.Subtype = SalesInvoice.Subtypes.Issued;
        await docs.SaveDocumentAsync(full);

        await context.GetService<ISalesFulfillmentService>()
            .MarkSourceOrderDeliveredAsync(full.MetaId);

        context.AddClientAction(ClientAction.Message("Реализация выставлена."));
    }
}
