using ZuloOne.Managers;

public partial class LoseLeadCommand
{
    public override async Task ExecuteAsync(SalesLead document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesLead>(document.MetaId);
        if (full == null) return;

        full.Subtype = SalesLead.Subtypes.Lost;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Лид отмечен как потерянный."));
    }
}
