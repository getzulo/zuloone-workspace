using ZuloOne.Managers;

// Mark-packed command: Picking → Packing.
// The reserve stays (SalesInvoiceReserveTx is bound to Packing as well).
public partial class MarkPackedCommand
{
    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesInvoice>(document.MetaId);
        if (full == null) return;
        full.Subtype = SalesInvoice.Subtypes.Packing;
        await docs.SaveDocumentAsync(full);
    }
}
