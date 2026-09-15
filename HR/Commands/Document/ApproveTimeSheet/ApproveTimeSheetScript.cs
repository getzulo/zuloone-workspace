// "Approve timesheet" command on the TimeSheet source subtype: transition to Approved.
// Domain checks live in OnBeforePost; here — empty document and subtype change.
// The engine replaces postings of the target state (Mix semantics).
public partial class ApproveTimeSheetCommand
{
    public override async Task ExecuteAsync(TimeSheet document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<TimeSheet>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя утвердить пустой табель: добавьте строки."));
            return;
        }

        full.Subtype = TimeSheet.Subtypes.Approved;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Табель утверждён."));
    }
}
