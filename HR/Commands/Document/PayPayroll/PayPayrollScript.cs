// Команда «Выплатить ФОТ» на подтипе-источнике PayrollPayment: переход в Paid.
// Проверки предметной области живут в OnBeforePost; здесь — пустой документ
// и смена подтипа. Движок заменяет проводки целевого состояния (семантика Mix).
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
