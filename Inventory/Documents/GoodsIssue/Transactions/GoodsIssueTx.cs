#nullable enable

// Realization/shipment — goods leave the warehouse "for sale". A SINGLE Stock
// movement (single-entry, like the warehouse register in MIQS): minus qty from
// the picking cell (FromCell, header). There is NO counter-leg in the stock
// register — the sale counterpart (revenue/receivable) lives in financial
// registers, not a fictitious cell. Guard against shipping over on-hand —
// in GoodsIssueEventHandler.OnBeforePostAsync.
//
// The register gets BaseQuantity — quantity in the item's BASE unit, which
// the platform computes when saving the line from (Quantity, Unit). Balance
// cannot be accumulated in mixed units: 2 boxes and 24 pieces would add up to 26.
// Zero here means "line unit not specified, no conversion" — the platform
// deliberately skips such a line, and the entered quantity IS the base. The
// same pair of lines sits in every warehouse posting; they no longer round
// themselves — the value arrives rounded to the unit's own precision.
public partial class GoodsIssueTx
{
    protected override void GetTransactions(GoodsIssue document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -qty));
        }
    }
}
