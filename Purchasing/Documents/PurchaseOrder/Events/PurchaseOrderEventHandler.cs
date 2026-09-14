#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Purchase order validation: a receipt must have lines and every line a positive
// quantity. Lines are re-loaded via IDocumentManager (the header event does not
// carry table parts).
public partial class PurchaseOrderEventHandler : TypedDocumentEventHandler<PurchaseOrder>
{
    public override async Task<EventResult> OnBeforePostAsync(PurchaseOrder document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<PurchaseOrder>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Заказ без строк не проводится");

        foreach (var line in lines)
        {
            if (line.Quantity <= 0m)
                return EventResult.Cancel("Количество в строке должно быть больше нуля");
        }

        // The tax contour IS configured, but there is no effective rate on the
        // receipt date — the receipt is not posted. Mirror of the sales-invoice
        // check, and it lives in a CANCELABLE event for the same reason: in
        // OnAfterPost, where the calculation itself is spawned, the platform turns
        // a handler refusal into a log warning: the document posts, and recoverable
        // input tax disappears silently.
        if (document.Subtype == "Received")
        {
            // Location discipline: receipt belongs in a RECEIVING cell; a put-away
            // task then moves the goods. The check asks Inventory, it does not
            // compare the cell-type name: the role set lives in metadata.
            // Discipline off (the default) — the service answers "any cell is fine",
            // and receipt behaves as before.
            var cells = context.GetService<IStoreCellService>();
            if (!await cells.IsCellAllowedForAsync(document.Location, StoreCellPurpose.Receiving))
                return EventResult.Cancel(
                    "Приход оформляется в ячейку ПРИЁМКИ — у выбранной ячейки другое назначение");

            var tax = context.GetService<ITaxService>();
            var taxCode = await tax.ResolveDefaultTaxCodeAsync();
            if (taxCode is not null && await tax.ResolveRateAsync(taxCode.Value, TaxPointOf(document)) is null)
                return EventResult.Cancel(
                    $"Налоговый код настроен, но действующей ставки на {TaxPointOf(document):yyyy-MM-dd} нет — приход не проводится");
        }

        return EventResult.Ok();
    }

    /// <summary>Tax-event date is the document date; an empty one is dated
    /// today, exactly as IDocumentManager stamps it on create.</summary>
    private static DateTime TaxPointOf(PurchaseOrder document)
        => document.DocumentDate == default ? DateTime.UtcNow.Date : document.DocumentDate.Date;

    // Receipting spawns an INPUT tax calculation — the mirror of output tax on
    // a sales invoice. Same service and the same optional contour: the only
    // difference is the direction code, so input and output cannot drift apart.
    // Input tax is recoverable, so it must land in the same ledger as output —
    // otherwise the return would compute tax payable on full revenue.
    public override async Task<EventResult> OnAfterPostAsync(PurchaseOrder document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Received") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var order = await docs.GetDocumentAsync<PurchaseOrder>(document.MetaId);
        if (order is null || order.Lines.Count == 0) return EventResult.Ok();

        var pricing = context.GetService<IPricingService>();
        var legalEntity = await context.GetService<IStoreCellService>().GetLegalEntityAsync(order.Location);
        if (legalEntity is not null)
        {
            var taxBase = order.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice));

            // The rate is resolved on the RECEIPT DATE, not today: otherwise the
            // document and its tax would be dated differently, and a backdated
            // receipt would be calculated at today's rate.
            var calc = await context.GetService<ITaxService>()
                .CreateCalculationAsync(legalEntity.Value, "INPUT", taxBase, $"Purchase order {document.Number}", TaxPointOf(document));
            if (calc.HasValue)
                await docs.AddLinkAsync(document.MetaId, calc.Value);

            await SpawnPutAwayTaskAsync(order, context);
        }

        return EventResult.Ok();
    }

    /// <summary>
    /// Received goods sit in the receiving cell and must move to storage — that
    /// is warehouse work, not accounting, so the receipt itself opens a DRAFT
    /// put-away task. A draft, not a posted one: the goods have not been moved
    /// physically yet; a person confirms.
    ///
    /// IDEMPOTENCY IS REQUIRED. The after-post event runs again on EVERY posting,
    /// and receipts are re-posted routinely — a packing-slip correction. Without
    /// the check a second posting would open a second task for the same goods
    /// (verified: with the check removed the test sees two). Separately, the
    /// doubling that hits SALES (the costing driver appends movements and forces
    /// the event to fire twice in one posting) is irrelevant here: a receipt
    /// increases stock, the driver fires on a net minus, there are no secondary
    /// movements.
    ///
    /// The key is the DOCUMENT GRAPH, not an id column: a document-to-document
    /// pointer on this platform is expressed as a link.
    ///
    /// Best-effort, like GL posting: no storage cell — no task, the receipt still
    /// posts. Otherwise an empty warehouse setting would fail purchasing.
    /// </summary>
    private static async Task SpawnPutAwayTaskAsync(PurchaseOrder order, EventContext context)
    {
        var cells = context.GetService<IStoreCellService>();
        if (!await cells.IsWarehouseDisciplineOnAsync()) return;

        var docs = context.GetService<IDocumentManager>();

        // An edge carries only endpoint ids; the type lives on the node — we match
        // one against the other. We look for an EDGE from this order, not any
        // "put-away" relative in the graph: the family walks links both ways for
        // eight steps, and a foreign task that joined it by a side path would
        // cancel creating our own.
        var family = await docs.GetDocumentFamilyAsync(order.MetaId);
        var putAwayIds = new HashSet<Guid>(
            family.Nodes.Where(n => n.DocTypeMetaId == PutAwayTaskType).Select(n => n.DocId));
        if (family.Edges.Any(e => e.ParentDocId == order.MetaId && putAwayIds.Contains(e.ChildDocId))) return;

        var store = await cells.GetStoreAsync(order.Location);
        if (store is null) return;
        var storageCell = await cells.SuggestStorageCellAsync(store.Value);
        if (storageCell is null) return;

        var task = await docs.NewDocumentAsync<PutAwayTask>("Draft", new Dictionary<string, object?>
        {
            ["FromCell"] = order.Location,
        });
        foreach (var line in order.Lines)
            task.Lines.Add(new PutAwayTaskLinesTablePartRow
            {
                Item = line.Item,
                Quantity = line.Quantity,
                Unit = line.Unit,
                ToCell = storageCell.Value,
            });

        await docs.SaveDocumentAsync(task);
        await docs.AddLinkAsync(order.MetaId, task.MetaId);
    }

    /// <summary>Put-away document type — used to find an already created task.</summary>
    private static readonly Guid PutAwayTaskType = Guid.Parse("57100701-0000-4000-8000-000000000000");
}
