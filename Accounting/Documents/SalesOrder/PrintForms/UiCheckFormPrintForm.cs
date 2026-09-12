public partial class UiCheckFormPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
    {
        return new SlimTable(new
        {
            Number = string.Empty,
            DocumentDate = DateTime.MinValue,
        });
    }

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var documents = context.GetService<IDocumentManager>();
        var header = await documents.GetDocumentHeaderAsync(context.DocumentTypeId, context.RecordId, context.CancellationToken);
        var table = new SlimTable("Report");
        if (header != null) table.Add(header);
        return table;
    }
}
