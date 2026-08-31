#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Инвентаризация по расхождению: на проведении читаем текущий остаток Stock по
// (ячейка, товар), считаем дельту = факт − система и двигаем Stock ОДИНОЧНОЙ
// проводкой на эту дельту (одинарная запись — как в StockAdjustment, без External).
// Проводки пишутся напрямую через IRegisterMovementService, привязанные к документу
// — движок снимет их при распроведении (DeleteDocumentMovements). Дельта считается
// здесь, а не в Tx, потому что текущий остаток доступен только через сервис, а
// транзакционный скрипт сервисов не видит.
//
// Факт берётся из BaseQuantity — CountedQty в БАЗОВОЙ единице товара, которую
// платформа считает при сохранении строки из пары (CountedQty, Unit). Дельта
// вычитает системный остаток, а он в базовой единице: пересчитать «2 ящика»
// нужно ДО вычитания, иначе инвентаризация спишет разницу, которой нет. Ноль =
// «единица не указана, пересчёта не было» → введённое количество и есть базовое.
public partial class StockCountEventHandler : TypedDocumentEventHandler<StockCount>
{

    public override async Task<EventResult> OnBeforePostAsync(StockCount header, EventContext context)
    {
        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<StockCount>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var stock = context.GetService<ITotalsManager>();

        foreach (var line in lines)
        {
            var bal = await stock.GetBalanceAsync("Stock",
                new Dictionary<string, object?> { ["Item"] = line.Item, ["Cell"] = header.Cell });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            var counted = line.BaseQuantity != 0m ? line.BaseQuantity : line.CountedQty;
            var delta = counted - onHand;
            if (delta == 0m) continue;

            await stock.PostMovementAsync("Stock", header.MetaId, header.CountDate == default ? DateTime.UtcNow : header.CountDate,
                new Dictionary<string, object?> { ["Item"] = line.Item, ["Cell"] = header.Cell },
                new Dictionary<string, decimal> { ["Qty"] = delta });
        }
        return EventResult.Ok();
    }
}
