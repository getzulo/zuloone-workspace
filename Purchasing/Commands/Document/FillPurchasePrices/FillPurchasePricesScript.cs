using System.Linq;

// Команда «Заполнить цены» на черновике заказа поставщику. Зеркало команды
// счёта: та же лестница, только сторона закупочная — прайс берётся у
// поставщика, а умолчание карточки — DefaultPurchasePrice.
//
// Заполняются только пустые цены: введённую руками цену (согласованную с
// поставщиком) подбор не трогает. Подробнее о том, почему это команда, а не
// автоподстановка при вводе строки — в FillSalesPricesScript.
public partial class FillPurchasePricesCommand
{
    public override async Task ExecuteAsync(PurchaseOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var pricing = context.GetService<IPricingService>();

        var full = await docs.GetDocumentAsync<PurchaseOrder>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("В документе нет строк."));
            return;
        }

        var filled = 0;
        var missing = 0;
        foreach (var line in full.Lines.Where(l => l.UnitPrice <= 0m))
        {
            var price = await pricing.ResolvePurchasePriceAsync(
                line.Item, line.Unit, full.Supplier, full.DocumentDate);

            if (price == null) { missing++; continue; }
            line.UnitPrice = price.Value;
            filled++;
        }

        if (filled == 0)
        {
            context.AddClientAction(ClientAction.Message(missing > 0
                ? $"Цены не найдены ни для одной из {missing} пустых строк."
                : "Все строки уже с ценой."));
            return;
        }

        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message(missing == 0
            ? $"Заполнено цен: {filled}."
            : $"Заполнено цен: {filled}; не найдено: {missing}."));
    }
}
