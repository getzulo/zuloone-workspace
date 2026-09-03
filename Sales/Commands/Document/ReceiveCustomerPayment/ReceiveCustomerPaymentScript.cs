using System.Linq;
using ZuloOne.Managers;

// «Принять оплату»: суммы строк. Аванс покупателя законен — остаток Receivable
// не проверяем.
public partial class ReceiveCustomerPaymentCommand
{
    public override async Task ExecuteAsync(CustomerPayment document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<CustomerPayment>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя принять пустую оплату: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Amount <= 0m))
        {
            context.AddClientAction(ClientAction.Message("Сумма оплаты должна быть больше нуля."));
            return;
        }

        full.Subtype = CustomerPayment.Subtypes.Paid;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Оплата покупателя проведена."));
    }
}
