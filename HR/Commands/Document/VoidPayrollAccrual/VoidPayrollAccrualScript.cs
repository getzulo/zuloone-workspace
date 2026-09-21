using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class VoidPayrollAccrualCommand
{
    public override async Task ExecuteAsync(PayrollAccrual document, CommandContext context)
    {
        var err = await context.GetService<IPayrollVoidService>().VoidAccrualAsync(document.MetaId);
        if (err != null)
        {
            context.AddClientAction(ClientAction.Message(err));
            return;
        }

        context.AddClientAction(ClientAction.Message("Начисление аннулировано."));
    }
}
