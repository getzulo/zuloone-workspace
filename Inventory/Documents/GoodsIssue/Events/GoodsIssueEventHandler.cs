#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for GoodsIssue (realization) documents.
// The document ships stock OUT of the warehouse to a sale — every line is a
// write-off of `Quantity` from FromCell. The posting itself is a single -qty
// Stock movement (see GoodsIssueTx); here we only guard against over-shipping.
public partial class GoodsIssueEventHandler : TypedDocumentEventHandler<GoodsIssue>
{

    // Before posting: reject a shipment that would drive a bin negative. Stock is a
    // single-entry register with allowNegativeBalance:true, so the engine will not
    // block it — we enforce "can't ship more than on-hand" here, per FromCell/Item.
    public override async Task<EventResult> OnBeforePostAsync(GoodsIssue header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<GoodsIssue>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;

        // Compared to the register balance, which is in the item's BASE unit — so
        // demand is also counted in BaseQuantity, otherwise "2 boxes" would pass
        // against 12 pieces on the shelf. Zero = unit not specified, no conversion.
        var need = new Dictionary<(Guid Cell, Guid Item), decimal>();
        foreach (var line in lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;

            // A negative line is REJECTED, not skipped. This used to be
            // `if (qty > 0m)` — negative lines never entered demand
            // and never hit the stock check. Meanwhile the transactional
            // script posts −qty, so a minus on the line became a PLUS in
            // the warehouse: a "−5" line received five units nobody
            // bought, and without a cost layer — Costing does not value
            // a positive net.
            if (qty <= 0m)
                return EventResult.Cancel("Количество отпуска должно быть больше нуля");

            var cell = line.FromCell != Guid.Empty ? line.FromCell : header.FromCell;
            var key = (cell, line.Item);
            need[key] = (need.TryGetValue(key, out var d) ? d : 0m) + qty;
        }

        var stock = context.GetService<ITotalsManager>();
        foreach (var kv in need)
        {
            var bal = await stock.GetBalanceAsync("Stock",
                new Dictionary<string, object?> { ["Item"] = kv.Key.Item, ["Cell"] = kv.Key.Cell });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Отгрузка сверх остатка: отгружается {kv.Value}, в наличии {onHand}");
        }
        return EventResult.Ok();
    }
}
