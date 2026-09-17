using ZuloOne.Managers;

// Команда «Начать отбор»: переход Reserved → Picking.
// Резерв остаётся (скрипт SalesInvoiceReserveTx привязан и к Picking).
public partial class StartPickingCommand
{
    public override async Task ExecuteAsync(SalesRealization document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesRealization>(document.MetaId);
        if (full == null) return;
        full.Subtype = SalesRealization.Subtypes.Picking;
        await docs.SaveDocumentAsync(full);
    }
}
