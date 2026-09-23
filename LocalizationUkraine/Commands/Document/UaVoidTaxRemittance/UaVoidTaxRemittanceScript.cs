using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class UaVoidTaxRemittanceCommand
{
    public override async Task ExecuteAsync(UaTaxRemittance document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<UaTaxRemittance>(document.MetaId);
        if (full == null) return;

        full.Subtype = UaTaxRemittance.Subtypes.Voided;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Перерахування анульовано."));
    }
}
