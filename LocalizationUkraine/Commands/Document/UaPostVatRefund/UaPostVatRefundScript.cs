using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class UaPostVatRefundCommand
{
    public override async Task ExecuteAsync(UaVatRefund document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<UaVatRefund>(document.MetaId);
        if (full == null) return;

        full.Subtype = UaVatRefund.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заяву на відшкодування ПДВ проведено."));
    }
}
