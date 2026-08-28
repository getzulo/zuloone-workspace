// «Ядерные тесты.Команды»: команда списка документов TBStockDoc — считает выборку.
public partial class TBStockDocTallyCommand
{
    public override async Task ExecuteAsync(IReadOnlyList<TBStockDoc> documents, CommandContext context)
    {
        context.AddClientAction(ClientAction.Message("docs=" + documents.Count));
        await Task.CompletedTask;
    }
}