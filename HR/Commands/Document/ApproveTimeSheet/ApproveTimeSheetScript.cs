// Команда «Утвердить табель» на подтипе-источнике TimeSheet: переход в Approved.
// Проверки предметной области живут в OnBeforePost; здесь — пустой документ
// и смена подтипа. Движок заменяет проводки целевого состояния (семантика Mix).
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
