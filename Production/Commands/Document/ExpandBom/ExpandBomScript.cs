// "Expand BOM" command on a production-order Draft subtype: fills the
// Components table part with BOM demand for the given finished-good quantity.
// Previously order lines were typed by hand even though the BOM is already
// described in BillOfMaterials/BomComponent.
//
// BOM expansion lives in BomService — the command is thin: check → expand →
// write. The script sits in THE SAME model as the service; if the own-model
// contract is unavailable at compile time, the logic will have to be called
// another way — compilation will tell.
public partial class ExpandBomCommand
{
    public override async Task ExecuteAsync(ProductionOrder document, CommandContext context)
    {
        if (document.Product == Guid.Empty || document.Quantity <= 0m)
        {
            context.AddClientAction(ClientAction.Message("Укажите изделие и количество больше нуля."));
            return;
        }

        // Expansion uses BASE quantity: the BOM is normalized to the finished
        // good's stock unit, while Quantity is entered in any (boxes, pallets).
        // Zero = no conversion, the unit is already base — the same cutoff as in
        // the postings.
        var bom = context.GetService<IBomService>();
        var outputQty = document.BaseQuantity != 0m ? document.BaseQuantity : document.Quantity;
        var need = await bom.ExpandByProductAsync(document.Product, outputQty);
        if (need.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Для изделия не найдена спецификация."));
            return;
        }

        // The document is re-read in full: the command header's table part is empty.
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<ProductionOrder>(document.MetaId);
        if (full == null) return;

        // Expansion REPLACES the lines: the command is the source of truth for demand.
        full.Components.Clear();
        foreach (var kv in need)
        {
            full.Components.Add(new ProductionOrderComponentsTablePartRow
            {
                Component = kv.Key,
                QtyRequired = kv.Value,
            });
        }

        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message($"Спецификация развёрнута: строк — {need.Count}."));
    }
}
