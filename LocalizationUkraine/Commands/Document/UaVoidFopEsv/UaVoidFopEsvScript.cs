using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class UaVoidFopEsvCommand
{
    public override async Task ExecuteAsync(UaFopEsvAccrual document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<UaFopEsvAccrual>(document.MetaId);
        if (full == null) return;

        full.Subtype = UaFopEsvAccrual.Subtypes.Voided;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Нарахування ЄСВ ФОП анульовано."));
    }
}
