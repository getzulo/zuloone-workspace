#nullable enable
using ZuloOne.Services.Contracts;

// Расширение Sales: при выставлении счёта клиент получает баллы лояльности,
// 1 балл за единицу валюты выручки. Сумма строки — общий PricingService, чтобы
// баллы, выручка и НДС считались от ОДНОЙ базы. Скрипт живёт в CRM и цепляется к
// подтипу SalesInvoice.Issued — движок исполняет его в цепочке проведения.
public partial class SalesLoyaltyTx
{
    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();

        decimal points = 0m;
        foreach (var line in document.Lines)
            points += pricing.LineAmount(line.Quantity, line.UnitPrice);

        if (points > 0m)
            transactions.Add(new RegisterMovementSpec("LoyaltyPoints")
                .An("Customer", document.Customer)
                .Res("Points", points));
    }
}
