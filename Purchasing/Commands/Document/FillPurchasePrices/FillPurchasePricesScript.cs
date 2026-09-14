using System.Linq;

// "Fill prices" command on a purchase-order draft. Mirror of the invoice
// command: the same ladder, purchase side only — the price list is taken from
// the vendor, and the card default is DefaultPurchasePrice.
//
// Only empty prices are filled: a price entered by hand (agreed with the
// vendor) is left alone. Why this is a command, not auto-fill on line entry —
// see FillSalesPricesScript.
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
