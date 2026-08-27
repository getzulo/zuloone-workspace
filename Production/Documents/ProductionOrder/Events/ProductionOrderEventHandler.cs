#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Core.Services;

namespace ZuloOne.Runtime.Generated;

// Валидация выпуска: производственный заказ не проводится без компонентов, с
// неположительным количеством и при нехватке компонента на ячейке. Компоненты и
// количество перечитываются через IDocumentManager (событие заголовка не несёт
// табличную часть). Проверка остатка — по физическим измерениям Stock через
// IRegisterMovementService (движковой проверки нет: Stock — ledger, allowNeg=true).
public partial class ProductionOrderEventHandler : TypedDocumentEventHandler<ProductionOrder>
{
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");

    public override async Task<EventResult> OnBeforePostAsync(ProductionOrder document, EventContext context)
    {
        if (document.Subtype != "Finished")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<ProductionOrder>(document.MetaId);
        var components = full?.Components ?? document.Components;
        var quantity = full?.Quantity ?? document.Quantity;
        var location = full?.Location ?? document.Location;

        if (quantity <= 0m)
            return EventResult.Cancel("Количество выпуска должно быть больше нуля");

        if (components.Count == 0)
            return EventResult.Cancel("Заполните компоненты (разверните спецификацию)");

        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in components)
            demand[line.Component] = (demand.TryGetValue(line.Component, out var d) ? d : 0m) + line.QtyRequired;

        var stock = context.GetService<IRegisterMovementService>();
        foreach (var kv in demand)
        {
            var bal = await stock.GetBalanceAsync(StockRegister,
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Location"] = location });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Недостаточно компонента на ячейке: требуется {kv.Value}, в наличии {onHand}");
        }

        return EventResult.Ok();
    }
}
