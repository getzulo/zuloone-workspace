#nullable enable
using System;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Tax-calculation integrity at FINALIZATION. Two checks, and their order is
// not accidental:
//
//   1. the line rate is THE one that was effective on the calculation's TaxPointDate;
//   2. amount = base × that rate (to money rounding).
//
// Arithmetic alone is not enough: a calculation assembled by hand or via API
// with ANY rate is self-consistent — 100 × 0.20 = 20 checks out no worse than
// 100 × 0.15 = 15. The check would pass, the number would go into the return,
// and the discrepancy would surface at the tax authority. The rate is the
// INPUT of the calculation and must be confirmed first; arithmetic after that
// is computed from the confirmed rate.
//
// Rate resolution is NOT repeated here: EffectiveFrom/EffectiveTo windows of
// the tax, the code and the rate itself are read by ITaxService.ResolveRateAsync.
// A second copy of that logic would drift from the first, and silently — the
// document would be calculated by one rule and checked by another.
//
// WHY OnBeforePost. Of the posting events the platform marks only this one as
// cancelable: DocumentPostingService calls OnBeforePost/OnBeforeUnpost with
// cancelable: true and turns a refusal into an exception, while
// OnAfterPost/OnAfterUnpost use cancelable: false, where a refusal becomes a
// log line and the document posts anyway. A check that MUST refuse can only
// live here.
//
// Lines are re-read via IDocumentManager: the header event arrives without
// table parts.
public partial class TaxCalculationEventHandler : TypedDocumentEventHandler<TaxCalculation>
{
    public override async Task<EventResult> OnBeforePostAsync(TaxCalculation document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<TaxCalculation>(document.MetaId);
        var calc = full ?? document;
        var lines = calc.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Налоговый расчёт без строк не финализируется");

        // The tax-event date is the one recorded on the calculation. That date,
        // not "today": a backdated calculation must be confirmed at its period's
        // rate, otherwise last year's document would be rejected because the rate
        // has changed since.
        var taxPoint = calc.TaxPointDate.Date;
        var tax = context.GetService<ITaxService>();

        foreach (var line in lines)
        {
            // There is NO rate on the date — refuse, do not "compute from what is
            // written". The line points at a code whose rate is not effective on
            // this date: either the date was swapped or the rate was withdrawn
            // retroactively. Neither grants the right to issue an amount that can
            // no longer be justified; silent assent here is the hole this is for.
            // Overlapping windows are thrown by ResolveRateAsync itself — the
            // platform turns a handler exception into a posting refusal too.
            var effective = await tax.ResolveRateAsync(line.TaxCode, taxPoint);
            if (effective is null)
                return EventResult.Cancel(
                    $"На {taxPoint:yyyy-MM-dd} у налогового кода строки нет действующей ставки — "
                    + "финализировать расчёт нечем");

            // Comparison is EXACT. Both values came from columns of the same EDT
            // TaxRateValue — decimal(9,6), so a mismatch can only be real; and on
            // a large base the sixth decimal place is money.
            if (line.RateOverridden != true && effective.Value != line.RateValue)
                return EventResult.Cancel(
                    $"Ставка строки {line.RateValue} не действовала на {taxPoint:yyyy-MM-dd}: "
                    + $"действующая ставка {effective.Value}");

            // Rounding is taken from the service: money precision is the global
            // AmountScale setting, and the calculation and its check must not have
            // two different opinions about how many digits the amount has.
            var expected = tax.CalculateTax(line.TaxBase, line.RateValue);
            if (Math.Abs(expected - line.TaxAmount) > 0.01m)
                return EventResult.Cancel($"Сумма налога {line.TaxAmount} не сходится с базой×ставкой ({expected})");
        }

        return EventResult.Ok();
    }
}
