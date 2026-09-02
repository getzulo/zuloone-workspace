#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for StockTransfer documents.
// `header` is a typed StockTransfer entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class StockTransferEventHandler : TypedDocumentEventHandler<StockTransfer>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override Task<EventResult> OnBeforeCreateAsync(StockTransfer header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(StockTransfer header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(StockTransfer header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(StockTransfer header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(StockTransfer header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(StockTransfer header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(StockTransfer header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before posting: validate the whole document; cancel to block posting.
    //
    // ЗАЩИТА ОТ УХОДА В МИНУС. Регистр Stock объявлен allowNegativeBalance=true —
    // движок минус не отклоняет, поэтому проверка обязана быть здесь. Она есть у
    // всех прочих расходных документов (списание, отпуск, отбор, раскладка,
    // продажа, выпуск), а у перемещения её не было.
    //
    // Почему это опаснее обычного ухода в минус: перемещение — ПАРА проводок по
    // одному товару, нетто ноль, поэтому драйвер себестоимости на него не смотрит
    // вовсе. Переместив 100 при остатке 5, получаем −95 в исходной ячейке и +100
    // в целевой БЕЗ слоя себестоимости, и последующая отгрузка из целевой съест
    // слои чужого, реально существующего товара.
    //
    // Потребность считается по BaseQuantity: остаток регистра ведётся в базовой
    // единице, и «2 ящика» иначе прошли бы проверку против 12 штук на полке.
    // Строки одного товара складываются — дроблением по строкам проверка не
    // обходится.
    public override async Task<EventResult> OnBeforePostAsync(StockTransfer header, EventContext context)
    {
        if (header.Subtype != "Posted")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<StockTransfer>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var from = full?.FromCell ?? header.FromCell;

        var need = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty <= 0m)
                return EventResult.Cancel("Количество перемещения должно быть больше нуля");
            need[line.Item] = (need.TryGetValue(line.Item, out var d) ? d : 0m) + qty;
        }

        var stock = context.GetService<ITotalsManager>();
        foreach (var kv in need)
        {
            var bal = await stock.GetBalanceAsync("Stock",
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Cell"] = from });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Перемещение сверх остатка: перемещается {kv.Value}, в наличии {onHand}");
        }

        return EventResult.Ok();
    }

    // After the document was posted (register movements are written).
    public override Task<EventResult> OnAfterPostAsync(StockTransfer header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(StockTransfer header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(StockTransfer header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override Task<EventResult> OnGenerateDescriptionAsync(StockTransfer header, EventContext context)
    {
        // context.Data["description"] = "StockTransfer " + header.Number;
        return Task.FromResult(EventResult.Ok());
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(StockTransfer header, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
