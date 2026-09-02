#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Расширение счёта продажи МОДЕЛЬЮ CRM: при выставлении клиент получает баллы.
// Сумма строки — общий PricingService, чтобы баллы, выручка и НДС считались от
// ОДНОЙ базы. Скрипт живёт в CRM и цепляется к подтипу SalesInvoice.Issued —
// движок исполняет его в цепочке проведения.
//
// КУРС И РУБИЛЬНИК — НАСТРОЙКА, А НЕ КОНСТАНТА В КОДЕ. CRMSettings объявляет
// PointsPerCurrencyUnit и LoyaltyEnabled; до этого ни то, ни другое не читалось
// ни одной строкой — курс был жёстко зашит 1:1, а «выключить лояльность» не
// выключало ничего.
//
// Настройки читаются ОДИН РАЗ в инициализаторе поля, а не внутри GetTransactions:
// экземпляр скрипта живёт одно проведение, так что это и есть «свежие настройки
// на каждое проведение», и обращение к БД происходит до того, как открыто
// соединение регистра (тот же приём, что в CostingValuationTotalDriver).
//
// СОВМЕСТИМОСТЬ. Записи настроек нет вовсе — модуль не настраивали, работаем как
// раньше: 1 балл за единицу валюты. Запись есть — она главнее кода во всём, включая
// выключенную лояльность. Нулевой курс в существующей записи трактуется как
// «курс не задан» (1:1), иначе заведение записи ради одного рубильника молча
// обнулило бы начисление.
public partial class SalesLoyaltyTx
{
    private readonly (bool Enabled, decimal Rate) _loyalty = ReadLoyaltySettings();

    private static (bool Enabled, decimal Rate) ReadLoyaltySettings()
    {
        var rows = GetService<IDictionaryManager>()
            .GetRecordsAsync<CRMSettings>(null, 1).GetAwaiter().GetResult();
        if (rows.Count == 0) return (true, 1m);

        var rate = rows[0].PointsPerCurrencyUnit;
        return (rows[0].LoyaltyEnabled, rate > 0m ? rate : 1m);
    }

    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        if (!_loyalty.Enabled) return;

        var pricing = GetService<IPricingService>();

        decimal amount = 0m;
        foreach (var line in document.Lines)
            amount += pricing.LineAmount(line.Quantity, line.UnitPrice, document.DiscountPercent);

        var points = Math.Round(amount * _loyalty.Rate, 2, MidpointRounding.AwayFromZero);

        if (points > 0m)
            transactions.Add(new RegisterMovementSpec("LoyaltyPoints")
                .Dim("Customer", document.Customer)
                .Res("Points", points));
    }
}
