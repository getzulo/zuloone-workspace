using System;

// Сервис "MeasurementService": контракт IMeasurementService. Единая точка
// округления количеств, весов и денежных сумм по настроенной точности
// (глобальные константы QuantityScale / WeightScale / AmountScale). Любой
// складской/производственный расчёт округляет здесь, а не «магическими» вызовами
// Math.Round с разбросанными по коду разрядами.
public partial class MeasurementService
{
    /// <summary>Округлить количество по QuantityScale (по умолчанию 3 знака).</summary>
    public decimal RoundQuantity(decimal value) => Round(value, GlobalConstants.Get<int?>("QuantityScale") ?? 3);

    /// <summary>Округлить вес по WeightScale (по умолчанию 3 знака).</summary>
    public decimal RoundWeight(decimal value) => Round(value, GlobalConstants.Get<int?>("WeightScale") ?? 3);

    /// <summary>Округлить денежную сумму по AmountScale (по умолчанию 2 знака).</summary>
    public decimal RoundAmount(decimal value) => Round(value, GlobalConstants.Get<int?>("AmountScale") ?? 2);

    /// <summary>Округление до заданного числа знаков (арифметическое, от нуля).</summary>
    public decimal Round(decimal value, int scale)
        => Math.Round(value, Math.Max(0, scale), MidpointRounding.AwayFromZero);
}
