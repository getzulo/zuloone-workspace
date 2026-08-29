#nullable enable
using ZuloOne.Services.Contracts;

// Дебиторка по выставленному счёту: покупатель должен сумму счёта.
// Скрипт привязан к подтипу Issued — и в этом вся механика погашения: при
// переходе «Выставлен → Оплачен» движок снимает проводки состояния Issued, долг
// исчезает сам, отдельной сторнирующей проводки не нужно. Выручка и списание со
// склада при этом сохраняются: их скрипты привязаны к ДОКУМЕНТУ, а не к подтипу.
public partial class SalesReceivableTx
{
    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Receivable")
                .An(Analytics.Receivable.Customer, document.Customer)
                .Res("Amount", pricing.LineAmount(line.Quantity, line.UnitPrice)));
        }
    }
}
