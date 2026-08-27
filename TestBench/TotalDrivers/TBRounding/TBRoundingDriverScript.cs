// Стендовый скриптовый драйвер итогов: база FifoTotalDriver, хук
// CalculatePartialAmount округляет частичную себестоимость до 2 знаков.
public partial class TBRoundingTotalDriver
{
    protected override decimal CalculatePartialAmount(decimal lotQuantity, decimal lotAmount, decimal transQuantity)
    {
        return System.Math.Round(base.CalculatePartialAmount(lotQuantity, lotAmount, transQuantity), 2, System.MidpointRounding.AwayFromZero);
    }
}