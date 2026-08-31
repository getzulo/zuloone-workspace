#nullable enable
using System;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Целостность налогового расчёта на ФИНАЛИЗАЦИИ. Две проверки, и порядок у них
// не случайный:
//
//   1. ставка строки — ТА, что действовала на TaxPointDate расчёта;
//   2. сумма = база × эта ставка (с точностью до денежного округления).
//
// Одной арифметики мало: расчёт, собранный руками или через API с ЛЮБОЙ ставкой,
// самосогласован — 100 × 0.20 = 20 сходится ничуть не хуже, чем 100 × 0.15 = 15.
// Проверка проходила, число уходило в декларацию, и расхождение всплывало у
// налогового органа. Ставка — ВХОД расчёта, её и надо подтверждать первой;
// арифметика после этого считается уже от подтверждённой ставки.
//
// Подбор ставки здесь НЕ повторяется: окна EffectiveFrom/EffectiveTo налога,
// кода и самой ставки читает ITaxService.ResolveRateAsync. Второй экземпляр этой
// логики разошёлся бы с первым, и разошёлся бы молча — документ считался бы по
// одному правилу, а проверялся по другому.
//
// ПОЧЕМУ в OnBeforePost. Из событий проводки платформа объявляет отменяемым
// только его: DocumentPostingService зовёт OnBeforePost/OnBeforeUnpost с
// cancelable: true и превращает отказ в исключение, а OnAfterPost/OnAfterUnpost —
// с cancelable: false, где отказ становится строкой в логе и документ проводится
// всё равно. Проверка, которая обязана ОТКАЗЫВАТЬ, может стоять только здесь.
//
// Строки перечитываются через IDocumentManager: шапочное событие приходит без
// табличных частей.
public partial class TaxCalculationEventHandler : TypedDocumentEventHandler<TaxCalculation>
{
    public override async Task<EventResult> OnBeforePostAsync(TaxCalculation document, EventContext context)
    {
        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<TaxCalculation>(document.MetaId);
        var calc = full ?? document;
        var lines = calc.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Налоговый расчёт без строк не финализируется");

        // Дата налогового события — та, что записана в расчёте. Именно она, а не
        // «сегодня»: расчёт, выпущенный задним числом, обязан подтверждаться
        // ставкой своего периода, иначе прошлогодний документ отвергался бы за то,
        // что ставка с тех пор поменялась.
        var taxPoint = calc.TaxPointDate.Date;
        var tax = context.GetService<ITaxService>();

        foreach (var line in lines)
        {
            // Ставки на дату НЕТ — отказ, а не «посчитаем по тому, что записано».
            // Строка ссылается на код, чья ставка на эту дату не действует: либо
            // дату подменили, либо ставку отозвали задним числом. Ни то ни другое
            // не даёт права выпустить сумму, обосновать которую больше нечем;
            // молчаливое согласие здесь и есть та дыра, ради которой всё это.
            // Пересечение окон ResolveRateAsync бросает сам — исключение
            // обработчика платформа тоже превращает в отказ проводки.
            var effective = await tax.ResolveRateAsync(line.TaxCode, taxPoint);
            if (effective is null)
                return EventResult.Cancel(
                    $"На {taxPoint:yyyy-MM-dd} у налогового кода строки нет действующей ставки — "
                    + "финализировать расчёт нечем");

            // Сравнение ТОЧНОЕ. Обе величины пришли из колонок одного EDT
            // TaxRateValue — decimal(9,6), так что расхождение может быть только
            // настоящим; а на большой базе оно и в шестом знаке стоит денег.
            if (effective.Value != line.RateValue)
                return EventResult.Cancel(
                    $"Ставка строки {line.RateValue} не действовала на {taxPoint:yyyy-MM-dd}: "
                    + $"действующая ставка {effective.Value}");

            // Округление берётся у сервиса: денежная точность — глобальная
            // настройка AmountScale, и у расчёта с его проверкой не должно быть
            // двух разных мнений о том, сколько в сумме знаков.
            var expected = tax.CalculateTax(line.TaxBase, effective.Value);
            if (Math.Abs(expected - line.TaxAmount) > 0.01m)
                return EventResult.Cancel($"Сумма налога {line.TaxAmount} не сходится с базой×ставкой ({expected})");
        }

        return EventResult.Ok();
    }
}
