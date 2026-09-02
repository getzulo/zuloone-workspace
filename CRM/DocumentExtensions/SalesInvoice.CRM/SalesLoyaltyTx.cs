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
// СОВМЕСТИМОСТЬ И ЛОВУШКА НЕОБЯЗАТЕЛЬНОГО BOOLEAN. LoyaltyEnabled объявлен
// необязательным, а необязательный Boolean в платформе НЕ nullable: «не
// заполнено» неотличимо от «выключено». Записи CRMSettings заводились до того,
// как флаг вообще начал читаться, поэтому у всех существующих он false. Если
// доверять ему буквально, правка молча выключила бы лояльность на каждом стенде,
// где кто-то однажды открыл и сохранил форму настроек — без единой ошибки в логе.
//
// Поэтому признаком «модуль настроен» служит НЕ флаг, а положительный курс:
//   записи нет вовсе          → работаем как раньше, 1 балл за единицу валюты;
//   запись есть, курс не задан → модуль лояльности не настраивали, тоже как раньше;
//   запись есть, курс задан    → настраивали осознанно, флаг и курс главнее кода.
// Так рубильник действительно выключает, но только у того, кто его осознанно
// трогал, а не у всех подряд.
public partial class SalesLoyaltyTx
{
    private readonly (bool Enabled, decimal Rate) _loyalty = ReadLoyaltySettings();

    private static (bool Enabled, decimal Rate) ReadLoyaltySettings()
    {
        var rows = GetService<IDictionaryManager>()
            .GetRecordsAsync<CRMSettings>(null, 1).GetAwaiter().GetResult();
        if (rows.Count == 0) return (true, 1m);

        var rate = rows[0].PointsPerCurrencyUnit;
        if (rate <= 0m) return (true, 1m);

        return (rows[0].LoyaltyEnabled, rate);
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
