using ZuloOne.Managers;

// Команда «Чек-аут»: закрывает визит (Open → Closed) и штампует время выхода.
public partial class CheckOutVisitCommand
{
    public override async Task ExecuteAsync(AgentVisit document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<AgentVisit>(document.MetaId);
        if (full == null) return;

        if (full.CheckedOutAt == default)
            full.CheckedOutAt = DateTime.UtcNow;

        full.Subtype = AgentVisit.Subtypes.Closed;
        await docs.SaveDocumentAsync(full);
    }
}
