// "Pay payroll" command on the PayrollPayment source subtype: transition to Paid.
// Domain checks live in OnBeforePost; here — empty document and subtype change.
// The engine replaces postings of the target state (Mix semantics).
public partial class PayPayrollCommand
{
    public override async Task ExecuteAsync(PayrollPayment document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<PayrollPayment>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя выплатить пустой документ: добавьте строки."));
            return;
        }

        full.Subtype = PayrollPayment.Subtypes.Paid;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Выплата ФОТ проведена."));
    }
}
