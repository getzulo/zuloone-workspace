// «Ядерные тесты.Скрипты»: эхо параметров с их рантайм-типами — проверяет
// типизированную инжекцию дефолтов MIQS ScriptUserParameter.
public partial class TBParamEchoCommand
{
    public override async Task ExecuteAsync(IDictionary<string, object?> parameters, IList<ClientAction> clientActions)
    {
        string Fmt(string name) => parameters.TryGetValue(name, out var v) && v != null
            ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) + "(" + v.GetType().Name + ")"
            : "null";
        clientActions.Add(ClientAction.Message(
            "rate=" + Fmt("Rate") + ";tag=" + Fmt("Tag") + ";note=" + Fmt("Note"), "info"));
        await Task.CompletedTask;
    }
}