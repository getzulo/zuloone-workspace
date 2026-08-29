#nullable enable
using ZuloOne.Services.Contracts;

// Признаёт кредиторку перед поставщиком по строке (сумма — общий PricingService,
// количество × цена), в разрезе поставщика.
public partial class GoodsReceiptPayableTx
{
    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Payable")
                .An(Analytics.Payable.Supplier, document.Supplier)
                .Res("Amount", pricing.LineAmount(line.Quantity, line.UnitPrice)));
        }
    }
}
