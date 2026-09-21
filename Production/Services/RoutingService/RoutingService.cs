using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Service "Routing": IRoutingService. Copies the live product route onto the
// order by MetaId — the generated entity is not on the contract (CS1503 across
// compile generations). Posting does not read these rows.
//
// Lookup goes through IDictionaryManager's Expression door (EntityMarshaler),
// not IDictionaryManager<T>.GetRecordsAsync() — that path JSON-roundtrips the
// bag and drops Guid columns, so Product/Routing never match in memory.
public partial class RoutingService
{
    private readonly IDictionaryManager _dicts;
    private readonly IDocumentManager _docs;

    public RoutingService(IDictionaryManager dicts, IDocumentManager docs)
    {
        _dicts = dicts;
        _docs = docs;
    }

    public async Task<int> StampOperationsAsync(Guid orderId)
    {
        if (orderId == Guid.Empty) return 0;
        var order = await _docs.GetDocumentAsync<ProductionOrder>(orderId);
        if (order == null || order.Product == Guid.Empty) return 0;

        order.Operations.Clear();

        var routing = (await _dicts.GetRecordsAsync<Routing>(
                r => r.Product == order.Product && !r.IsDisabled))
            .FirstOrDefault();
        if (routing != null)
        {
            var qty = order.BaseQuantity != 0m ? order.BaseQuantity : order.Quantity;
            if (qty < 0m) qty = 0m;

            var steps = (await _dicts.GetRecordsAsync<RoutingOperation>(
                    s => s.Routing == routing.MetaId))
                .OrderBy(s => s.Sequence)
                .ToList();
            foreach (var step in steps)
            {
                order.Operations.Add(new ProductionOrderOperationsTablePartRow
                {
                    Sequence = step.Sequence,
                    Name = step.Name,
                    WorkCenter = step.WorkCenter,
                    SetupMinutes = step.SetupMinutes,
                    RunMinutes = step.RunMinutesPerUnit * qty,
                });
            }
        }

        var count = order.Operations.Count;
        await _docs.SaveDocumentAsync(order);
        return count;
    }
}
