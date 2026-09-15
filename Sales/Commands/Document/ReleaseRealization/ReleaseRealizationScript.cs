using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Команда «Выставить реализацию»: переход Shipped → Issued.
// Проверки остатка, юрлица и налоговой ставки — в OnBeforePost.
// После перехода: Mix снимает резерв SalesInvoiceReserveTx и
// записывает Stock/Revenue/Receivable/Loyalty/SaudiVat скриптами Issued.
// Заказ-источник переводится в Delivered здесь (после SaveDocumentAsync),
// а не в OnAfterPost — там вложенный SetSubtypeAsync глотается безмолвно.
public partial class ReleaseRealizationCommand
{
    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesInvoice>(document.MetaId);
        if (full == null) return;

        full.Subtype = SalesInvoice.Subtypes.Issued;
        await docs.SaveDocumentAsync(full);

        await context.GetService<ISalesFulfillmentService>()
            .MarkSourceOrderDeliveredAsync(full.MetaId);

        context.AddClientAction(ClientAction.Message("Реализация выставлена."));
    }
}
