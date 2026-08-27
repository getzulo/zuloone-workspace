// ══ ДВИЖОК ПЛАТФОРМЫ — только чтение ══
// Оригинальный MIQS-драйвер, вкомпилированный в ZuloOne. Правки — в исходниках
// платформы (src/ZuloOne.Core/Server/totals/Calculation), не в воркспейсе:
// файл перезаписывается при каждом экспорте и в компиляцию не попадает.


using ZuloOne.ClassDescriptors;
using System;

namespace ZuloOne.Server.Totals.Calculation
{
    public class MoneyTotalDriver : FifoTotalDriver
    {
        public MoneyTotalDriver(TotalDescriptor td)
          : base(td, "CurrencyAmount")
        {
        }

        protected override Decimal CalculatePartialAmount(
          Decimal lotQuantity,
          Decimal lotAmount,
          Decimal transQuantity)
        {
            return Math.Round(base.CalculatePartialAmount(lotQuantity, lotAmount, transQuantity), 2);
        }
    }
}
