// Команда «Оплатить взносы» на подтипе-источнике SocialInsurancePayment: переход в Paid.
// Проверки предметной области живут в OnBeforePost; здесь — пустой документ
// и смена подтипа. Движок заменяет проводки целевого состояния (семантика Mix).
public partial class PaySocialInsuranceCommand
{
    public override async Task ExecuteAsync(SocialInsurancePayment document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SocialInsurancePayment>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя оплатить пустой документ: добавьте строки."));
            return;
        }

        full.Subtype = SocialInsurancePayment.Subtypes.Paid;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Оплата взносов проведена."));
    }
}
