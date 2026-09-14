// DISABLED (isEnabled: false), left as a documented dead end.
//
// The idea "payment = invoice subtype" is wrong: on Issued → Paid the engine
// lifts the PREVIOUS state's movements, so REVENUE was zeroed together with
// receivable — payment cancelled the sale. Caught by ReceivableFlowTest
// ("revenue survives payment, actual 0.00").
//
// The correct model is a separate CustomerPayment document (like a payout in HR):
// the invoice stays issued, and the payment settles the debt with its own movement.
// The object cannot be deleted from the database (no DELETE for document commands),
// so the command is disabled.
public partial class MarkPaidCommand
{
    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        context.AddClientAction(ClientAction.Message(
            "Команда отключена: оплату проводите документом «Оплата покупателя» (CustomerPayment)."));
        await Task.CompletedTask;
    }
}
