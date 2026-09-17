// "TestBench.Commands": TBWarehouse dictionary command — the typed hook
// receives the loaded record and returns its name as a client message.
public partial class TBWarehouseInfoCommand
{
    public override async Task ExecuteAsync(TBWarehouse record, CommandContext context)
    {
        context.AddClientAction(ClientAction.Message("warehouse=" + record.Name));
        await Task.CompletedTask;
    }
}
