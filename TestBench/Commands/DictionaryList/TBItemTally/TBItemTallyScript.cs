// "TestBench.Commands": TBItem list command — counts the selection.
public partial class TBItemTallyCommand
{
    public override async Task ExecuteAsync(IReadOnlyList<TBItem> records, CommandContext context)
    {
        context.AddClientAction(ClientAction.Message("count=" + records.Count));
        await Task.CompletedTask;
    }
}
