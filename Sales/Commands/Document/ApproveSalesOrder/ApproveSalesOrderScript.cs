using ZuloOne.Services.Contracts;

// ApproveSalesOrder: Submitted → Confirmed. Checks live in ConfirmOrderAsync
// so Submit with ConfirmOrderOnSubmit cannot drift.
public partial class ApproveSalesOrderCommand
{
    public override async Task ExecuteAsync(SalesOrder document, CommandContext context)
    {
        var error = await context.GetService<ISalesFulfillmentService>().ConfirmOrderAsync(document.MetaId);
        if (error != null)
        {
            context.AddClientAction(ClientAction.Message(error));
            return;
        }

        context.AddClientAction(ClientAction.Message("Заказ согласован."));
    }
}
