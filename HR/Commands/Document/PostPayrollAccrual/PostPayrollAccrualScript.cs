// Команда «Провести начисление» на подтипе-источнике PayrollAccrual: переход в Posted.
// Проверки предметной области живут в OnBeforePost; здесь — пустой документ
// и смена подтипа. Движок заменяет проводки целевого состояния (семантика Mix).
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
