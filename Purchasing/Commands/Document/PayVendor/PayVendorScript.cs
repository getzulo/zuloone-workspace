using System.Linq;
using ZuloOne.Managers;

// «Оплатить поставщику»: суммы строк. Остаток Payable не режем — аванс законен.
public partial class PayVendorCommand
{
    public override async Task ExecuteAsync(VendorPayment document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<VendorPayment>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя оплатить пустой документ: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Amount <= 0m))
        {
            context.AddClientAction(ClientAction.Message("Сумма оплаты должна быть больше нуля."));
            return;
        }

        full.Subtype = VendorPayment.Subtypes.Paid;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Оплата поставщику проведена."));
    }
}
