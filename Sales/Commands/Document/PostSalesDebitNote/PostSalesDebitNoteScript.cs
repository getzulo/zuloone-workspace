using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class PostSalesDebitNoteCommand
{
    public override async Task ExecuteAsync(SalesDebitNote document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesDebitNote>(document.MetaId);
        if (full == null) return;

        if (full.OriginalInvoice == Guid.Empty)
        {
            context.AddClientAction(ClientAction.Message("Укажите исходный счёт."));
            return;
        }
        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустую дебет-ноту: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("Количество в строке должно быть больше нуля."));
            return;
        }

        full.Subtype = SalesDebitNote.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Дебет-нота проведена."));
    }
}
