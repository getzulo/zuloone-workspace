using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class UaPostFopEsvCommand
{
    public override async Task ExecuteAsync(UaFopEsvAccrual document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<UaFopEsvAccrual>(document.MetaId);
        if (full == null) return;

        full.Subtype = UaFopEsvAccrual.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("ЄСВ ФОП за себе нараховано."));
    }
}
