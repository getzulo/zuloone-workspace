#nullable enable
using ZuloOne.Services.Contracts;

// FIFO costing: receiving a purchase order creates a lot in the
// ItemCostFifo register — +Quantity and +Amount per line. The FIFO-engine
// register stores layers by item; on issue (−Quantity, Amount = 0) the
// engine itself computes disposal cost from the oldest lots and rejects
// over-issue. The movement is typed: Item is a physical register dimension,
// not an analytic. Lot amount comes from the shared PricingService.
//
// ═══ THE ONLY PLACE IN THE TREE WHERE TWO CONVENTIONS MEET IN ONE
// OPERATOR, AND THE TWO ARGUMENTS DELIBERATELY READ DIFFERENT FIELDS OF
// THE SAME LINE:
//
//   Quantity ← BaseQuantity (the item's BASE unit). This is WAREHOUSE QTY:
//     FIFO layers are written off by an issue that will come from warehouse
//     postings, and those are in the base unit too. Take entered «5 boxes»
//     here — and a lot of 5 will meet an issue of 60 pieces: the engine
//     will declare over-issue on an item that is physically on the shelf.
//
//   Amount ← Quantity (ENTERED quantity) × UnitPrice. This is MONEY, and
//     the price is for THE SAME UNIT the quantity was entered in: 5 boxes
//     × price per box. Recalculate here too — the lot amount grows by
//     exactly the pack factor, and inventory cost drifts from the supplier
//     invoice.
//
// Result: Amount/Quantity yields the BASE-unit price — exactly what FIFO
// must use to value disposal. Zero BaseQuantity means «the line unit was
// not set, there was no conversion» — then the entered quantity is the
// base one, and both arguments honestly match.
//
// There is no quantity rounding of its own here anymore: the value arrives
// already rounded to the unit's own precision. RoundAmount/RoundWeight in
// other scripts stay — only the second QUANTITY-rounding path was removed.
public partial class ReceiptFifoTx
{
    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new ItemCostFifo
            {
                Item = line.Item,
                Quantity = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity,
                Amount = pricing.LineAmount(line.Quantity, line.UnitPrice)
            });
        }
    }
}
