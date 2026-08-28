#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Два правила заказа. Первое — автоподстановка компонентов: если в настройках
// модуля включён AutoExpandBom, новый заказ сам разворачивает спецификацию, а
// одноимённая команда остаётся ручной альтернативой (перезаполнить после смены
// количества). Второе — валидация выпуска: заказ не проводится без компонентов,
// с неположительным количеством и при нехватке компонента на ячейке.
//
// Компоненты и количество перечитываются через IDocumentManager (событие
// заголовка не несёт табличную часть). Проверка остатка — по физическим
// измерениям Stock через IRegisterMovementService (движковой проверки нет:
// Stock — ledger, allowNegativeBalance=true).
public partial class ProductionOrderEventHandler : TypedDocumentEventHandler<ProductionOrder>
{
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");

    public override async Task<EventResult> OnAfterSaveAsync(ProductionOrder header, bool isNew, EventContext context)
    {
        // Только при создании: сохранение развёрнутых строк ниже вызовет это же
        // событие повторно, и без отсечки получилась бы рекурсия. Уже заполненные
        // строки не трогаем — ручной ввод важнее настройки.
        if (!isNew || header.Product == Guid.Empty || header.Quantity <= 0m)
            return EventResult.Ok();

        var settings = (await context.GetService<IDictionaryManager<ProductionSettings>>()
            .GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (settings == null || !settings.AutoExpandBom) return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<ProductionOrder>(header.MetaId);
        if (full == null || full.Components.Count > 0) return EventResult.Ok();

        var need = await context.GetService<IBomService>().ExpandByProductAsync(header.Product, header.Quantity);
        if (need.Count == 0) return EventResult.Ok();

        foreach (var kv in need)
            full.Components.Add(new ProductionOrderComponentsTablePartRow { Component = kv.Key, QtyRequired = kv.Value });

        await docs.SaveDocumentAsync(full);
        return EventResult.Ok();
    }

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
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Cell"] = location });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Недостаточно компонента на ячейке: требуется {kv.Value}, в наличии {onHand}");
        }

        return EventResult.Ok();
    }
}
