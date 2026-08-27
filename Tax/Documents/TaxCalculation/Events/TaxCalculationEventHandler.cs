#nullable enable
namespace ZuloOne.Runtime.Generated;

// Tax calculation integrity: on finalization, every line's amount must equal
// base × rate (within a rounding cent). Lines are re-loaded via IDocumentManager
// because the header event receives the document without its table parts.
public partial class TaxCalculationEventHandler : TypedDocumentEventHandler<TaxCalculation>
{
    public override async Task<EventResult> OnBeforePostAsync(TaxCalculation document, EventContext context)
    {
        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<TaxCalculation>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Налоговый расчёт без строк не финализируется");

        foreach (var line in lines)
        {
            var expected = Math.Round(line.TaxBase * line.RateValue, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(expected - line.TaxAmount) > 0.01m)
                return EventResult.Cancel($"Сумма налога {line.TaxAmount} не сходится с базой×ставкой ({expected})");
        }

        return EventResult.Ok();
    }
}
