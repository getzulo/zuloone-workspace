using System.Linq;

// Команда «Заказать» на подтипе Draft заказа поставщику: управляемый переход
// Черновик → Заказано. Раньше заказ прыгал из черновика сразу в приход, и
// состояния «размещён у поставщика, но ещё не приехал» просто не было.
//
// Проверка перед переходом: в заказе должны быть строки с положительным
// количеством. Не проходит — сообщение пользователю и документ остаётся на месте.
public partial class PlaceOrderCommand
{
    public override async Task ExecuteAsync(PurchaseOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();

        // Строки у заголовка из команды пусты — документ перечитывается.
        var full = await docs.GetDocumentAsync<PurchaseOrder>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя заказать пустой документ: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("В каждой строке количество должно быть больше нуля."));
            return;
        }

        full.Subtype = PurchaseOrder.Subtypes.Ordered;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заказ размещён у поставщика."));
    }
}
