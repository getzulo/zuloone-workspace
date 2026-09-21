using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class VoidSocialInsurancePaymentCommand
{
    public override async Task ExecuteAsync(SocialInsurancePayment document, CommandContext context)
    {
        var err = await context.GetService<IPayrollVoidService>().VoidSocialInsurancePaymentAsync(document.MetaId);
        if (err != null)
        {
            context.AddClientAction(ClientAction.Message(err));
            return;
        }

        context.AddClientAction(ClientAction.Message("Платёж в фонд аннулирован."));
    }
}
