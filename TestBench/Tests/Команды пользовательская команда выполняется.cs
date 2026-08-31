// «Ядерные тесты.Команды»: глобальная UserCommand исполняется через
// CommandFamilyService (тот же диспатч, что и /api/commands2/user/{id}/execute).
//
// Тест данных не заводит: обе операции ниже — поиск команды и её ЗАПУСК — и есть
// предмет проверки. Менеджера над диспатчем семейств команд в платформе нет
// (CommandFamilyService — сервис Core, не тир менеджеров), поэтому обращение
// идёт через харнесс осознанно, а не по умолчанию.
public partial class TbUserCommandTests
{
    [IntegrationTest("Команды: пользовательская команда выполняется")]
    public async Task UserCommandExecutes()
    {
        var commandId = await Db.FindCommandIdAsync("user", "TBUserPing");
        var result = await Db.ExecuteUserCommandAsync(commandId);
        Assert.IsTrue(result.Success, "команда должна выполниться: {0}", result.Message ?? "");
        Assert.IsTrue(result.ClientMessages.Contains("user-ok"),
            "маркер «user-ok» среди клиентских сообщений: {0}", string.Join("; ", result.ClientMessages));
        Assert.IsTrue(result.ClientActionCount >= 1, "команда вернула хотя бы одно клиентское действие");
        Log("UserCommand выполнена: " + result.ClientActionCount + " действий, маркер user-ok получен.");
    }
}
