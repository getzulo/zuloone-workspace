using ZuloOne.Managers;

// Команда «Отгружено»: переход Packing → Shipped.
// Резерв сохраняется (скрипт SalesInvoiceReserveTx привязан и к Shipped).
public partial class MarkShippedCommand
{
    public override async Task ExecuteAsync(SalesRealization document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesRealization>(document.MetaId);
        if (full == null) return;
        full.Subtype = SalesRealization.Subtypes.Shipped;
        await docs.SaveDocumentAsync(full);
    }
}
