#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;

namespace ZuloOne.Runtime.Generated;

// Инвентаризация по расхождению: на проведении читаем текущий остаток Stock по
// (ячейка, товар), считаем дельту = факт − система и двигаем Stock ПАРОЙ двойной
// записи против External-бакета (излишек приходит из External, недостача уходит во
// External). Проводки пишутся напрямую через IRegisterMovementService, привязанные
// к документу — движок снимет их при распроведении (DeleteDocumentMovements).
// Дельта считается здесь, а не в Tx, потому что текущий остаток доступен только
// через сервис, а транзакционный скрипт сервисов не видит.
public partial class StockCountEventHandler : TypedDocumentEventHandler<StockCount>
{
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");
    private static readonly Guid External = Guid.Parse("e0000000-0000-4000-8000-0000000000e1");

    public override async Task<EventResult> OnBeforePostAsync(StockCount header, EventContext context)
    {
        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<StockCount>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var stock = context.GetService<IRegisterMovementService>();

        foreach (var line in lines)
        {
            var bal = await stock.GetBalanceAsync(StockRegister,
                new Dictionary<string, object?> { ["Item"] = line.Item, ["Cell"] = header.Cell });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            var delta = line.CountedQty - onHand;
            if (delta == 0m) continue;

            var pair = Guid.NewGuid();
            await stock.PostMovementAsync(StockRegister, header.MetaId, header.CountDate == default ? DateTime.UtcNow : header.CountDate,
                new Dictionary<string, object?> { ["Item"] = line.Item, ["Cell"] = header.Cell },
                new Dictionary<string, decimal> { ["Qty"] = delta },
                pairMetaId: pair);
            await stock.PostMovementAsync(StockRegister, header.MetaId, header.CountDate == default ? DateTime.UtcNow : header.CountDate,
                new Dictionary<string, object?> { ["Item"] = line.Item, ["Cell"] = External },
                new Dictionary<string, decimal> { ["Qty"] = -delta },
                pairMetaId: pair);
        }
        return EventResult.Ok();
    }
}
