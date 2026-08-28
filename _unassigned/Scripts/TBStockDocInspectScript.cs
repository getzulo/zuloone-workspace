// «Ядерные тесты.Команды»: команда документа TBStockDoc (привязана только к
// подтипу Receipt) — типизированный хук читает документ и возвращает его подтип.
public partial class TBStockDocInspectCommand
{
    public override async Task ExecuteAsync(TBStockDoc document, CommandContext context)
    {
        context.AddClientAction(ClientAction.Message("subtype=" + document.Subtype));
        await Task.CompletedTask;
    }
}