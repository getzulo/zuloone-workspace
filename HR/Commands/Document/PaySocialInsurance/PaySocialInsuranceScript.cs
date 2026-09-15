// "Pay contributions" command on the SocialInsurancePayment source subtype: transition to Paid.
// Domain checks live in OnBeforePost; here — empty document and subtype change.
// The engine replaces postings of the target state (Mix semantics).
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
