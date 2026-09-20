using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class PostPurchaseCreditNoteCommand
{
    public override async Task ExecuteAsync(PurchaseCreditNote document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<PurchaseCreditNote>(document.MetaId);
        if (full == null) return;

        if (full.OriginalOrder == Guid.Empty)
        {
            context.AddClientAction(ClientAction.Message("Укажите исходный заказ."));
            return;
        }
        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустую кредит-ноту: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("Количество в строке должно быть больше нуля."));
            return;
        }

        full.Subtype = PurchaseCreditNote.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Кредит-нота закупки проведена."));
    }
}
