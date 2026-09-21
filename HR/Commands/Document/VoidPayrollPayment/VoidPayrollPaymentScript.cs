using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class VoidPayrollPaymentCommand
{
    public override async Task ExecuteAsync(PayrollPayment document, CommandContext context)
    {
        var err = await context.GetService<IPayrollVoidService>().VoidPaymentAsync(document.MetaId);
        if (err != null)
        {
            context.AddClientAction(ClientAction.Message(err));
            return;
        }

        context.AddClientAction(ClientAction.Message("Выплата аннулирована."));
    }
}
