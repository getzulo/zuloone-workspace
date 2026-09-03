// Команда «Отменить заказ» на подтипе-источнике SalesOrder: переход в Cancelled.
// Проверки предметной области живут в OnBeforePost; здесь — пустой документ
// и смена подтипа. Движок заменяет проводки целевого состояния (семантика Mix).
public partial class CancelSalesOrderCommand
{
    public override async Task ExecuteAsync(SalesOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesOrder>(document.MetaId);
        if (full == null) return;

        full.Subtype = SalesOrder.Subtypes.Cancelled;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заказ отменён."));
    }
}
