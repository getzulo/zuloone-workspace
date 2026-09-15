using ZuloOne.Managers;

// Команда «Начать отбор»: переход Reserved → Picking.
// Резерв остаётся (скрипт SalesInvoiceReserveTx привязан и к Picking).
public partial class StartPickingCommand
{
    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesInvoice>(document.MetaId);
        if (full == null) return;
        full.Subtype = SalesInvoice.Subtypes.Picking;
        await docs.SaveDocumentAsync(full);
    }
}
