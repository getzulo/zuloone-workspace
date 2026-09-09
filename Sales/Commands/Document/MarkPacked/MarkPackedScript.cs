using ZuloOne.Managers;

// Команда «Упаковано»: переход Picking → Packing.
// Резерв сохраняется (скрипт SalesInvoiceReserveTx привязан и к Packing).
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
