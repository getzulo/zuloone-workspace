// «Ядерные тесты.Скрипты»: MIQS ScriptUserParameter — дефолты инжектятся
// типизированно ДО запуска скрипта, отсутствие обязательного значения без
// дефолта отклоняет выполнение.
public partial class TbScriptParamTests
{
    [IntegrationTest("дефолты параметров инжектятся типизированно")]
    public async Task DefaultsInjected()
    {
        var commandId = await Db.FindCommandIdAsync("user", "TBParamEcho");
        var result = await Db.ExecuteUserCommandAsync(commandId,
            new Dictionary<string, object?> { ["Note"] = "hi" });
        Assert.IsTrue(result.Success, "команда должна выполниться: {0}", result.Message ?? "");
        Assert.IsTrue(result.ClientMessages.Contains("rate=2.5(Decimal);tag=x(String);note=hi(String)"),
            "эхо параметров с типами: {0}", string.Join("; ", result.ClientMessages));
        Log("Дефолты Rate=2.5 (Decimal) и Tag=x (String) инжектированы, Note дошёл как передан.");
    }

    [IntegrationTest("required без значения и дефолта отклоняется")]
    public async Task RequiredEnforced()
    {
        var commandId = await Db.FindCommandIdAsync("user", "TBParamEcho");
        var result = await Db.ExecuteUserCommandAsync(commandId);
        Assert.IsFalse(result.Success, "без Note команда не должна выполниться");
        Assert.IsTrue((result.Message ?? "").Contains("Note"),
            "отказ называет параметр: {0}", result.Message ?? "");
        Log("Обязательный Note без дефолта отклонён до запуска скрипта.");
    }
}