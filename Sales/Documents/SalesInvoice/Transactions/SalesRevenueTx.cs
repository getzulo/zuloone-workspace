#nullable enable
using ZuloOne.Services.Contracts;

// Признаёт выручку по строке в разрезе товара и клиента. Сумма строки считается
// общим PricingService (количество × цена, округлённое до денежной точности).
public partial class SalesRevenueTx
{
    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Revenue")
                .An("Item", line.Item)
                .An("Customer", document.Customer)
                .Res("Amount", pricing.LineAmount(line.Quantity, line.UnitPrice)));
        }
    }
}
