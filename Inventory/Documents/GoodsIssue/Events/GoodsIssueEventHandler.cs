#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Core.Services;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for GoodsIssue (Реализация) documents.
// The document ships stock OUT of the warehouse to a sale — every line is a
// write-off of `Quantity` from FromCell. The posting itself is a single -qty
// Stock movement (see GoodsIssueTx); here we only guard against over-shipping.
public partial class GoodsIssueEventHandler : TypedDocumentEventHandler<GoodsIssue>
{
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");

    // Before posting: reject a shipment that would drive a bin negative. Stock is a
    // single-entry register with allowNegativeBalance:true, so the engine will not
    // block it — we enforce "can't ship more than on-hand" here, per FromCell/Item.
    public override async Task<EventResult> OnBeforePostAsync(GoodsIssue header, EventContext context)
    {
        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<GoodsIssue>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;

        // Сравнивается с остатком регистра, а он в БАЗОВОЙ единице товара — значит и
        // потребность считается по BaseQuantity, иначе «2 ящика» прошли бы проверку
        // против 12 штук на полке. Ноль = единица не указана, пересчёта не было.
        var need = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty > 0m)
                need[line.Item] = (need.TryGetValue(line.Item, out var d) ? d : 0m) + qty;
        }

        var stock = context.GetService<IRegisterMovementService>();
        foreach (var kv in need)
        {
            var bal = await stock.GetBalanceAsync(StockRegister,
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Cell"] = header.FromCell });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Отгрузка сверх остатка: отгружается {kv.Value}, в наличии {onHand}");
        }
        return EventResult.Ok();
    }
}
