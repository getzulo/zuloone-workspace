#nullable enable
using ZuloOne.Services.Contracts;

// Кредит-нота со стороны первого события: она уменьшает БАЗУ договора, а налог
// освобождает драйвер — так же, как начисляет.
//
// ПОЧЕМУ НЕ МИНУС ПРЯМО В UaVatPayable, как было раньше. Тот вариант давал
// верную цифру налога и при этом ломал модель: UaVatFirstEvent сохранял Shipped
// и Accrued, которые кредит-нота уже сторнировала. Планка max(Shipped, Paid)
// оставалась завышенной, и СЛЕДУЮЩАЯ отгрузка по тому же договору не начисляла
// ничего, пока не перекрывала застрявший уровень. Ошибка проявлялась не сразу и
// не на кредит-ноте, а на документе через один — худший вид расхождения.
//
// Пишем базу БЕЗ налога и без торговой точки — ровно как отгрузка. Юрлицо, как и
// у отгрузки, берётся с документа: обработчик кредит-ноты копирует его с
// исходного счёта, так что сторно всегда попадает в координату того же продавца,
// который начислял. Разъедься они — освобождение легло бы мимо начисления.
public partial class UaCreditVatTx
{
    protected override void GetTransactions(SalesCreditNote document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();

        decimal baseAmount = 0m;
        foreach (var line in document.Lines)
            baseAmount += pricing.LineAmount(line.Quantity, line.UnitPrice);

        if (baseAmount == 0m) return;

        transactions.Add(new RegisterMovementSpec("UaVatFirstEvent")
            .Dim("LegalEntity", document.LegalEntity)
            .Dim("Customer", document.Customer)
            .Dim("SalesContract", document.Contract)
            .Res("Shipped", -baseAmount));
    }
}
