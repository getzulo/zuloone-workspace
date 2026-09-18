using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class IssueQuotationCommand
{
    public override async Task ExecuteAsync(SalesQuotation document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesQuotation>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя выставить пустое КП: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("В каждой строке количество должно быть больше нуля."));
            return;
        }

        var onDate = full.DeliveryDate != default ? full.DeliveryDate : DateTime.UtcNow;
        var pair = await context.GetService<ISalesContractService>().ValidatePairAsync(
            full.Customer, full.Outlet, full.Contract, onDate);
        if (pair != null)
        {
            context.AddClientAction(ClientAction.Message(pair));
            return;
        }

        full.Subtype = SalesQuotation.Subtypes.Issued;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("КП выставлено."));
    }
}
