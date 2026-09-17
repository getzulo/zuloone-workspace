using ZuloOne.Managers;

// Команда «Чек-ин»: открывает день агента (Draft → Open).
// Визит по конкретной точке заводит push-действие LogVisit — это web-путь.
public partial class CheckInVisitCommand
{
    public override async Task ExecuteAsync(VisitRoute document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<VisitRoute>(document.MetaId);
        if (full == null) return;

        if (full.Subtype == VisitRoute.Subtypes.Draft)
            full.Subtype = VisitRoute.Subtypes.Open;

        await docs.SaveDocumentAsync(full);
    }
}
