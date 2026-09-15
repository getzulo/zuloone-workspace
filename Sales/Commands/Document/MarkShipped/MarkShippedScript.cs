using ZuloOne.Managers;

// Mark-shipped command: Packing → Shipped.
// The reserve stays (SalesInvoiceReserveTx is bound to Shipped as well).
public partial class MarkShippedCommand
{
    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesInvoice>(document.MetaId);
        if (full == null) return;
        full.Subtype = SalesInvoice.Subtypes.Shipped;
        await docs.SaveDocumentAsync(full);
    }
}
