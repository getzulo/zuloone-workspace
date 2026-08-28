// «Ядерные тесты.Команды»: команда справочника TBWarehouse — типизированный хук
// получает загруженную запись и возвращает её имя клиентским сообщением.
public partial class TBWarehouseInfoCommand
{
    public override async Task ExecuteAsync(TBWarehouse record, CommandContext context)
    {
        context.AddClientAction(ClientAction.Message("warehouse=" + record.Name));
        await Task.CompletedTask;
    }
}