// «Ядерные тесты.Команды»: глобальная пользовательская команда — маркер «user-ok».
public partial class TBUserPingCommand
{
    public override async Task ExecuteAsync(IDictionary<string, object?> parameters, IList<ClientAction> clientActions)
    {
        clientActions.Add(ClientAction.Message("user-ok", "success"));
        await Task.CompletedTask;
    }
}