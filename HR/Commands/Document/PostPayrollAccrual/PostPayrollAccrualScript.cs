// "Post accrual" command on the PayrollAccrual source subtype: transition to Posted.
// Domain checks live in OnBeforePost; here — empty document and subtype change.
// The engine replaces postings of the target state (Mix semantics).
public partial class PostPayrollAccrualCommand
{
    public override async Task ExecuteAsync(PayrollAccrual document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<PayrollAccrual>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустое начисление: добавьте строки."));
            return;
        }

        full.Subtype = PayrollAccrual.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Начисление проведено."));
    }
}
