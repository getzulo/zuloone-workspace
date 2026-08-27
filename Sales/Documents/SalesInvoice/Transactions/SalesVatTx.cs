#nullable enable
using ZuloOne.Services.Contracts;

// Локализация КСА: при выставлении счёта начисляется НДС по ставке из глобальной
// константы SaudiVatRate (15%). База строки — общий PricingService, САМ налог —
// TaxService.CalculateTax (база × ставка с округлением): налоговый расчёт живёт в
// налоговом сервисе, а не размазан по проводкам. Скрипт цепляется к подтипу
// SalesInvoice.Issued наравне с проводками Sales и CRM.
public partial class SalesVatTx
{
    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        var tax = GetService<ITaxService>();
        var rate = GlobalConstants.Get<decimal>("SaudiVatRate");

        decimal baseAmount = 0m;
        foreach (var line in document.Lines)
            baseAmount += pricing.LineAmount(line.Quantity, line.UnitPrice);

        var vat = tax.CalculateTax(baseAmount, rate);
        if (vat > 0m)
            transactions.Add(new RegisterMovementSpec("VatPayable")
                .An("Customer", document.Customer)
                .Res("Amount", vat));
    }
}
