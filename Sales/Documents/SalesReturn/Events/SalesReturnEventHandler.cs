#nullable enable
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class SalesReturnEventHandler : TypedDocumentEventHandler<SalesReturn>
{
    public override async Task<EventResult> OnBeforePostAsync(SalesReturn document, EventContext context)
    {
        if (document.Subtype != "Posted")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesReturn>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;
        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки возврата");
        if (lines.Any(l => l.Quantity <= 0m))
            return EventResult.Cancel("Количество в строке должно быть больше нуля");

        return EventResult.Ok();
    }
}
