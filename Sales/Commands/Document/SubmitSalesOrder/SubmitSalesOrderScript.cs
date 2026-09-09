using ZuloOne.Managers;

// Команда «Отправить заказ»: переход Draft → Submitted.
// Лёгкая проверка наличия строк; всё остальное — на согласовании (ApproveSalesOrder).
public partial class SubmitSalesOrderCommand
{
    public override async Task ExecuteAsync(SalesOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesOrder>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя отправить пустой заказ: добавьте строки."));
            return;
        }

        full.Subtype = SalesOrder.Subtypes.Submitted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заказ отправлен на согласование."));
    }
}
