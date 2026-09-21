using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class VoidSocialInsuranceAccrualCommand
{
    public override async Task ExecuteAsync(SocialInsuranceAccrual document, CommandContext context)
    {
        var err = await context.GetService<IPayrollVoidService>().VoidSocialInsuranceAccrualAsync(document.MetaId);
        if (err != null)
        {
            context.AddClientAction(ClientAction.Message(err));
            return;
        }

        context.AddClientAction(ClientAction.Message("Взносы аннулированы."));
    }
}
