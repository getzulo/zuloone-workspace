using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// "Fill prices" command on a draft invoice: sets UnitPrice from the customer's
// price list, and where that is missing — from the item card default.
//
// Bulk fill for empty prices on an already-entered invoice. New lines fill
// live via SalesInvoiceLinesEventHandler.OnFieldChanged (Item / Unit) — this
// command remains for "fill every blank row at once" after import or paste.
//
// ONLY empty prices are filled. A price typed by hand is a human decision
// (agreed discount, disputed item) and must not be overwritten by lookup.
// Anyone who needs a re-lookup clears the price and clicks again.
public partial class FillSalesPricesCommand
{
    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var pricing = context.GetService<IPricingService>();

        // Lines on the command header are empty — the document is re-read.
        var full = await docs.GetDocumentAsync<SalesInvoice>(document.MetaId);
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
            // Document date, not today: re-issuing a March invoice in May
            // must take the March price.
            decimal? price = null;
            if (full.Contract != Guid.Empty)
            {
                var contract = await context.GetService<IDictionaryManager<SalesContract>>()
                    .GetRecordAsync(full.Contract);
                if (contract is not null && contract.PriceType != Guid.Empty)
                    price = await pricing.ResolveForTypeAsync(
                        line.Item, line.Unit, contract.PriceType, full.DocumentDate);
            }
            price ??= await pricing.ResolveSalePriceAsync(
                line.Item, line.Unit, full.Customer, full.DocumentDate);

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
