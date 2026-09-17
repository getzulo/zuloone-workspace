// "TestBench.Commands": TBStockDoc document command (Receipt subtype only) —
// the typed hook reads the document and returns its subtype.
public partial class TBStockDocInspectCommand
{
    public override async Task ExecuteAsync(TBStockDoc document, CommandContext context)
    {
        context.AddClientAction(ClientAction.Message("subtype=" + document.Subtype));
        await Task.CompletedTask;
    }
}
