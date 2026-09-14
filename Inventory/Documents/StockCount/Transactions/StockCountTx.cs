#nullable enable

// Delta is already on the line (QtyDelta): the handler computed it when the
// draft was saved. Here only the Stock movement — the CostingIssue driver will
// see a minus and write off lots; a plus is picked up by ISurplusCostingService.
public partial class StockCountTx
{
    protected override void GetTransactions(StockCount document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            if (line.QtyDelta == 0m) continue;
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Item)
                .Dim("Cell", document.Cell)
                .Res("Qty", line.QtyDelta));
        }
    }
}
