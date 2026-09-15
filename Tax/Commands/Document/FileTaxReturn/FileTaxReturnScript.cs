using ZuloOne.Managers;

// "File return": legal entity is required. SubmitReturnAsync — OnAfterPost Filed,
// do not call from here (the mock / filing channel runs on posting).
public partial class FileTaxReturnCommand
{
    public override async Task ExecuteAsync(TaxReturn document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<TaxReturn>(document.MetaId);
        if (full == null) return;

        if (full.LegalEntity == Guid.Empty)
        {
            context.AddClientAction(ClientAction.Message("Укажите юридическое лицо декларации."));
            return;
        }

        full.Subtype = TaxReturn.Subtypes.Filed;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Декларация сдана."));
    }
}
