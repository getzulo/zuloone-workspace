#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Три правила заказа. Первое — автоподстановка компонентов: если в настройках
// модуля включён AutoExpandBom, новый заказ сам разворачивает спецификацию, а
// одноимённая команда остаётся ручной альтернативой (перезаполнить после смены
// количества). Второе — валидация выпуска: заказ не проводится без компонентов,
// с неположительным количеством и при нехватке компонента на ячейке. Третье —
// оценка выпуска: драйвер Costing на Stock уже списывает себестоимость
// потреблённых компонентов (чистый минус по товару), но партию изделию не
// заводит — положительный нетто не его забота (это может быть и приход, и
// безстоимостный излишек). Эту партию заводит сам заказ, симметрично тому, как
// ReceiptCostTx/ReceiptFifoTx заводят её закупке.
//
// Компоненты и количество перечитываются через IDocumentManager (событие
// заголовка не несёт табличную часть). Проверка остатка — по физическим
// измерениям Stock через ITotalsManager (движковой проверки нет: Stock —
// ledger, allowNegativeBalance=true).
public partial class ProductionOrderEventHandler : TypedDocumentEventHandler<ProductionOrder>
{

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

        // По БАЗОВОМУ количеству и из перечитанного документа: спецификация нормирована
        // на складскую единицу изделия, а в шапке количество может быть в любой.
        var outputQty = full.BaseQuantity != 0m ? full.BaseQuantity : full.Quantity;
        var need = await context.GetService<IBomService>().ExpandByProductAsync(full.Product, outputQty);
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

        // Сравнивается с остатком регистра, а он в БАЗОВОЙ единице товара — значит и
        // потребность считается по BaseQuantity. Ноль = единица не указана, пересчёта
        // не было (так приходят строки, развёрнутые из спецификации: BomService уже
        // отдаёт потребность в складской единице компонента).
        var demand = ComponentDemand(components);

        var stock = context.GetService<ITotalsManager>();
        foreach (var kv in demand)
        {
            var bal = await stock.GetBalanceAsync("Stock",
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Cell"] = location });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Недостаточно компонента на ячейке: требуется {kv.Value}, в наличии {onHand}");
        }

        return EventResult.Ok();
    }

    // Списание компонентов Costing уже провёл (Stock-нетто отрицательный —
    // выбытие). Изделию партии нет: заводим её сама, средней стоимостью
    // потреблённых компонентов (Amount/Quantity в ItemCostFifo на момент выпуска —
    // компонент без партий стоит 0, ровно как и при обычном списании).
    public override async Task<EventResult> OnAfterPostAsync(ProductionOrder document, EventContext context)
    {
        if (document.Subtype != "Finished")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<ProductionOrder>(document.MetaId);
        var components = full?.Components ?? document.Components;
        var product = full?.Product ?? document.Product;
        var quantity = full?.Quantity ?? document.Quantity;
        var baseQuantity = full?.BaseQuantity ?? document.BaseQuantity;
        var outputQty = baseQuantity != 0m ? baseQuantity : quantity;

        var totals = context.GetService<ITotalsManager>();
        var totalCost = 0m;
        foreach (var kv in ComponentDemand(components))
        {
            var key = new Dictionary<string, object?> { ["Item"] = kv.Key };
            var qtyOnHand = await totals.GetBalanceAsync("ItemCostFifo", "Quantity", key);
            if (qtyOnHand <= 0m) continue;

            var amtOnHand = await totals.GetBalanceAsync("ItemCostFifo", "Amount", key);
            totalCost += Math.Round(amtOnHand / qtyOnHand * kv.Value, 2, MidpointRounding.AwayFromZero);
        }

        var movementDate = DateTime.UtcNow.Date;
        var outputKey = new Dictionary<string, object?> { ["Item"] = product };
        await totals.PostMovementAsync("ItemCostFifo", document.MetaId, movementDate, outputKey,
            new Dictionary<string, decimal> { ["Quantity"] = outputQty, ["Amount"] = totalCost });

        // InventoryValue разрезан динамической аналитикой Item — тем же путём,
        // которым CostingIssueTotalDriver зеркалит списание (см. этот драйвер).
        var movements = context.GetService<IRegisterMovementService>();
        var inventoryValueId = (await context.GetService<IMetadataService>().GetAllRegistersAsync())
            .First(r => string.Equals(r.Name, "InventoryValue", StringComparison.OrdinalIgnoreCase)).MetaId;
        await movements.PostMovementAsync(inventoryValueId, document.MetaId, movementDate,
            new Dictionary<string, object?>(),
            new Dictionary<string, decimal> { ["Qty"] = outputQty, ["Value"] = totalCost },
            analytics: new Dictionary<string, object?> { ["Item"] = product });

        return EventResult.Ok();
    }

    private static Dictionary<Guid, decimal> ComponentDemand(IEnumerable<ProductionOrderComponentsTablePartRow> components)
    {
        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in components)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.QtyRequired;
            demand[line.Component] = (demand.TryGetValue(line.Component, out var d) ? d : 0m) + qty;
        }
        return demand;
    }
}
