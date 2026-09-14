// "TestBench.Commands": TBStockDoc list command — counts the selection.
public partial class TBStockDocTallyCommand
{
    public override async Task ExecuteAsync(IReadOnlyList<TBStockDoc> documents, CommandContext context)
    {
        context.AddClientAction(ClientAction.Message("docs=" + documents.Count));
        await Task.CompletedTask;
    }
}
