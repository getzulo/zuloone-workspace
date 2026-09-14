// Bench script totals driver: base FifoTotalDriver, hook
// CalculatePartialAmount rounds partial cost to 2 decimal places.
public partial class TBRoundingTotalDriver
{
    protected override decimal CalculatePartialAmount(decimal lotQuantity, decimal lotAmount, decimal transQuantity)
    {
        return System.Math.Round(base.CalculatePartialAmount(lotQuantity, lotAmount, transQuantity), 2, System.MidpointRounding.AwayFromZero);
    }
}