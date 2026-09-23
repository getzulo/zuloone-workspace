using ZuloOne.Services.Contracts;

// «Принять товар»: ячейка ПРИЁМКИ и действующая ставка налога на дату прихода.
// Проверки и сам переход Ordered → Received живут в IPurchaseReceiptService —
// той же дверью ходит «Заказать» при AutoReceiveOnOrder. Копия проверок стояла
// здесь ровно до тех пор, пока у правила не появилась вторая дверь: разойтись
// две копии успели бы молча, а расхождение видно только на проводке.
// CreateCalculationAsync / задание раскладки — OnAfterPost, отсюда не зовём.
public partial class ReceiveOrderCommand
{
    public override async Task ExecuteAsync(PurchaseOrder document, CommandContext context)
    {
        var error = await context.GetService<IPurchaseReceiptService>().ReceiveAsync(document.MetaId);
        if (error != null)
        {
            context.AddClientAction(ClientAction.Message(error));
            return;
        }

        context.AddClientAction(ClientAction.Message("Товар принят."));
    }
}
