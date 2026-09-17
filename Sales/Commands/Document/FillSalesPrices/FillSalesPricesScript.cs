using System;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// "Fill prices" command on a draft invoice: contract type + overlays via
// ISalesLinePricing, then IPricingService. ONLY empty prices are filled.
public partial class FillSalesPricesCommand
{
    public override async Task ExecuteAsync(SalesRealization document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var fill = context.GetService<ISalesLinePricing>();

        var full = await docs.GetDocumentAsync<SalesRealization>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("В документе нет строк."));
            return;
        }

        var filled = 0;
        var missing = 0;
        var onDate = full.DocumentDate != default ? full.DocumentDate : DateTime.UtcNow;
        foreach (var line in full.Lines.Where(l => l.UnitPrice <= 0m))
        {
            var resolved = await fill.ResolveForDocumentAsync(
                line.Item, line.Unit, line.Quantity, full.Customer, full.Contract, onDate);
            if (resolved["Price"] == null) { missing++; continue; }
            line.UnitPrice = Convert.ToDecimal(resolved["Price"]);
            line.PriceExplanation = resolved["Explanation"] as string ?? "";
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
