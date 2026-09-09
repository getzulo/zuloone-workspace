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
    private static readonly Guid SalesOrderType = Guid.Parse("23643b1b-b959-4206-83ab-948c713276c9");

    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesInvoice>(document.MetaId);
        if (full == null) return;

        full.Subtype = SalesInvoice.Subtypes.Issued;
        await docs.SaveDocumentAsync(full);

        // Закрываем заказ-источник: счёт выставлен → заказ Delivered.
        // Вызывается после SaveDocumentAsync (вне транзакции проводки счёта),
        // иначе вложенный SetSubtypeAsync из OnAfterPost глотается платформой.
        var sourceOrder = full.SourceOrder;
        if (sourceOrder != Guid.Empty)
        {
            var order = await docs.GetDocumentAsync<SalesOrder>(sourceOrder);
            if (order is not null && order.Subtype == SalesOrder.Subtypes.Confirmed)
            {
                await context.GetService<IDocumentPostingService>()
                    .SetSubtypeAsync(SalesOrderType, sourceOrder, SalesOrder.Subtypes.Delivered);
            }
        }

        context.AddClientAction(ClientAction.Message("Реализация выставлена."));
    }
}
