#nullable enable

// Дельта уже на строке (QtyDelta): обработчик посчитал её при сохранении
// черновика. Здесь только проводка в Stock — драйвер CostingIssue увидит минус
// и спишет партии; плюс подхватит ISurplusCostingService.
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
