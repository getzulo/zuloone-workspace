using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Команда «Отменить заказ»: переход Submitted|Confirmed → Cancelled.
// Если к заказу привязан счёт не в статусе Issued, он тоже отменяется
// (иначе реализация осталась бы зависшей с ненулевым резервом).
public partial class CancelSalesOrderCommand
{
    private static readonly Guid SalesInvoiceType = Guid.Parse("34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3");

    public override async Task ExecuteAsync(SalesOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesOrder>(document.MetaId);
        if (full == null) return;

        // Cancel linked realization invoice if it exists and has not yet been issued.
        var posting = context.GetService<IDocumentPostingService>();
        var invoices = await docs.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{full.MetaId}'");
        foreach (var inv in invoices)
        {
            if (inv.Subtype != "Issued")
                await posting.SetSubtypeAsync(SalesInvoiceType, inv.MetaId, "Cancelled");
        }

        full.Subtype = SalesOrder.Subtypes.Cancelled;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заказ отменён."));
    }
}
