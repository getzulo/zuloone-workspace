using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class UaPayTaxRemittanceCommand
{
    public override async Task ExecuteAsync(UaTaxRemittance document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<UaTaxRemittance>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Не можна перерахувати порожній документ: додайте рядки."));
            return;
        }

        full.Subtype = UaTaxRemittance.Subtypes.Paid;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Податок перераховано до бюджету."));
    }
}
