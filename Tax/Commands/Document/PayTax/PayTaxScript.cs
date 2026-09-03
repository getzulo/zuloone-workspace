using System.Linq;
using ZuloOne.Managers;

// «Уплатить налог»: суммы строк. SubmitPaymentAsync — OnAfterPost Paid.
public partial class PayTaxCommand
{
    public override async Task ExecuteAsync(TaxPayment document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<TaxPayment>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя уплатить пустой документ: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Amount <= 0m))
        {
            context.AddClientAction(ClientAction.Message("Сумма оплаты должна быть больше нуля."));
            return;
        }

        full.Subtype = TaxPayment.Subtypes.Paid;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Налог уплачен."));
    }
}
