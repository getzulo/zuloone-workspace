using ZuloOne.Managers;

public partial class CancelQuotationCommand
{
    public override async Task ExecuteAsync(SalesQuotation document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesQuotation>(document.MetaId);
        if (full == null) return;

        full.Subtype = SalesQuotation.Subtypes.Cancelled;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("КП отменено."));
    }
}
