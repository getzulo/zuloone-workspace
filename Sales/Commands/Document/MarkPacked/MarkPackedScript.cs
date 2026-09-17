using ZuloOne.Managers;

// Команда «Упаковано»: переход Picking → Packing.
// Резерв сохраняется (скрипт SalesRealizationReserveTx привязан и к Packing).
public partial class MarkPackedCommand
{
    public override async Task ExecuteAsync(SalesRealization document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesRealization>(document.MetaId);
        if (full == null) return;
        full.Subtype = SalesRealization.Subtypes.Packing;
        await docs.SaveDocumentAsync(full);
    }
}
