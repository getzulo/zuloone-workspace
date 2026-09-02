#nullable enable
using ZuloOne.Services.Contracts;

// НДС Саудовской Аравии на выставлении счёта — начисление в СВОЙ регистр
// локализации VatPayable.
//
// ПОЧЕМУ ЭТОТ СКРИПТ ЖИВЁТ ЗДЕСЬ, А НЕ В SALES. Раньше он лежал в модели Sales
// под именем SalesVatTx — то есть страновая логика была прописана в
// универсальном модуле продаж. Компилятор этого не ловил: и константа ставки, и
// регистр адресуются СТРОКАМИ (`GlobalConstants.Get("SaudiVatRate")`,
// `RegisterMovementSpec("VatPayable")`), а проверка зависимостей между моделями
// работает по типам. Слой продавили ровно там, где у платформы нет контроля, и
// у клиента в другой стране этот код всё равно исполнялся на каждом счёте —
// молча давая ноль, потому что саудовской константы у него нет.
//
// Теперь владелец — модель локализации: она зависит от Sales (а не наоборот),
// и её объекты уезжают вместе со страновым пакетом. Счёт при этом не тронут:
// скрипт цепляется к подтипу Issued САМ, документ его не перечисляет.
//
// Универсальный контур это НЕ отменяет: тот же налог независимо попадает в
// Tax.TaxLedger через TaxCalculation, который порождает событие счёта по
// правилам определения. Здесь — страновой срез для отчётности ZATCA.
//
// База строки — общий PricingService, САМ налог — TaxService.CalculateTax (база
// × ставка с округлением): налоговый расчёт живёт в налоговом сервисе, а не
// размазан по проводкам.
//
// СТАВКА БЕРЁТСЯ С ДОКУМЕНТА, а не из константы. Раньше здесь стояло
// `GlobalConstants.Get<decimal>("SaudiVatRate")` — плоское 0.15 БЕЗ ДАТЫ, второй
// источник истины рядом с датированным справочником TaxRate. Пока ставка не
// менялась, разницы не было; в день изменения TaxLedger пошёл бы по новой
// ставке, а VatPayable остался бы на старой, и расходились бы они молча. Счёт,
// выставленный задним числом, и вовсе считался бы здесь по сегодняшней ставке,
// а в универсальном контуре — по действовавшей.
//
// Теперь `TaxRateApplied` фиксирует на счёте ставку, подобранную налоговым
// контуром на дату документа (SalesInvoiceEventHandler.OnBeforePost). Ноль
// означает, что налоговый контур не настроен, — тогда проводки просто нет, как
// и раньше при отсутствующей константе.
public partial class SaudiVatTx
{
    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        var tax = GetService<ITaxService>();
        var rate = document.TaxRateApplied;
        if (rate <= 0m) return;

        decimal baseAmount = 0m;
        foreach (var line in document.Lines)
            baseAmount += pricing.LineAmount(line.Quantity, line.UnitPrice, document.DiscountPercent);

        var vat = tax.CalculateTax(baseAmount, rate);
        if (vat > 0m)
            transactions.Add(new RegisterMovementSpec("VatPayable")
                .An(Analytics.VatPayable.Customer, document.Customer)
                .Res("Amount", vat));
    }
}
