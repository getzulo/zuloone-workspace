using ZuloOne.Managers;

public partial class QualifyLeadCommand
{
    public override async Task ExecuteAsync(SalesLead document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesLead>(document.MetaId);
        if (full == null) return;
        if (string.IsNullOrWhiteSpace(full.Subject))
        {
            context.AddClientAction(ClientAction.Message("Укажите тему лида."));
            return;
        }

        full.Subtype = SalesLead.Subtypes.Qualified;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Лид квалифицирован."));
    }
}
