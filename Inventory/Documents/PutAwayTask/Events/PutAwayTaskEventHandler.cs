#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for PutAwayTask documents.
// `header` is a typed PutAwayTask entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class PutAwayTaskEventHandler : TypedDocumentEventHandler<PutAwayTask>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override Task<EventResult> OnBeforeCreateAsync(PutAwayTask header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(PutAwayTask header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(PutAwayTask header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(PutAwayTask header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(PutAwayTask header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(PutAwayTask header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(PutAwayTask header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Раскладка принятого: приёмка → хранение. Две проверки разной природы, и их
    // НЕЛЬЗЯ смешивать.
    //
    // Первая — физика: нельзя разложить больше, чем лежит в ячейке приёмки. Она
    // работает ВСЕГДА, флагом не выключается. Регистр Stock допускает
    // отрицательный остаток (allowNegativeBalance), поэтому движок здесь не
    // помощник — проверка обязана стоять тут.
    //
    // Вторая — политика: приёмка это приёмка, а хранение это хранение. Она
    // включается настройкой EnforceWarehouseTasks, потому что до сих пор ячейки
    // были свободными, и разом запретить произвольную ячейку значит сломать все
    // существующие документы.
    //
    // Политика живёт в StoreCellService, хотя он в ЭТОЙ ЖЕ модели: обращение
    // модели к собственному контракту сервиса когда-то ломало сборку контрактов,
    // но платформа это починила — проверено компиляцией и тестами. Поэтому здесь
    // тонкая оркестровка, а знание «какая ячейка для чего» — в одном месте, и
    // приход с продажей спрашивают то же самое.
    public override async Task<EventResult> OnBeforePostAsync(PutAwayTask header, EventContext context)
    {
        if (header.Subtype != "Confirmed") return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<PutAwayTask>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var fromCell = full?.FromCell ?? header.FromCell;

        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки задания");

        var cells = context.GetService<IStoreCellService>();
        var enforcing = await cells.IsWarehouseDisciplineOnAsync();
        if (enforcing && !await cells.IsCellAllowedForAsync(fromCell, StoreCellPurpose.Receiving))
            return EventResult.Cancel("Раскладка забирает товар из ячейки ПРИЁМКИ — у выбранной ячейки другое назначение");

        // Спрос считается по BaseQuantity — регистр хранит базовую единицу товара.
        // Ноль означает «единица не указана, пересчёта не было».
        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            if (enforcing && !await cells.IsCellAllowedForAsync(line.ToCell, StoreCellPurpose.Storage))
                return EventResult.Cancel("Раскладка кладёт товар в ячейку ХРАНЕНИЯ — у выбранной ячейки другое назначение");

            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty <= 0m) return EventResult.Cancel("Количество в строке должно быть больше нуля");
            demand[line.Item] = (demand.TryGetValue(line.Item, out var d) ? d : 0m) + qty;
        }

        var totals = context.GetService<ITotalsManager>();
        foreach (var kv in demand)
        {
            var onHand = await totals.GetBalanceAsync("Stock", "Qty",
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Cell"] = fromCell });
            if (kv.Value > onHand)
                return EventResult.Cancel($"Недостаточно товара в ячейке приёмки: требуется {kv.Value}, в наличии {onHand}");
        }

        return EventResult.Ok();
    }

    // After the document was posted (register movements are written).
    public override Task<EventResult> OnAfterPostAsync(PutAwayTask header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(PutAwayTask header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(PutAwayTask header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override Task<EventResult> OnGenerateDescriptionAsync(PutAwayTask header, EventContext context)
    {
        // context.Data["description"] = "PutAwayTask " + header.Number;
        return Task.FromResult(EventResult.Ok());
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(PutAwayTask header, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
