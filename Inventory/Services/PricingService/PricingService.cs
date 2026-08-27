using System;

// Сервис "PricingService": контракт IPricingService. Единая точка расчёта суммы
// строки документа (количество × цена), округлённой до денежной точности.
// Раньше `line.Quantity * line.UnitPrice` копипастилось по проводкам Sales,
// Purchasing, CRM, Localization, Costing и в событии GL — теперь одна формула.
// Точность берётся из глобальной константы AmountScale (та же настройка, что у
// MeasurementService).
public partial class PricingService
{
    /// <summary>Сумма строки = количество × цена, округлённая до денежной точности.</summary>
    public decimal LineAmount(decimal quantity, decimal unitPrice)
        => Math.Round(quantity * unitPrice, GlobalConstants.Get<int?>("AmountScale") ?? 2, MidpointRounding.AwayFromZero);
}
