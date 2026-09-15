#nullable enable
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class SalesReturnEventHandler : TypedDocumentEventHandler<SalesReturn>
{
    public override async Task<EventResult> OnBeforeSaveAsync(SalesReturn header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;

        if (header.OriginalInvoice != Guid.Empty
            && (header.Outlet == Guid.Empty || header.Contract == Guid.Empty))
        {
            var invoice = await context.GetService<IDocumentManager>()
                .GetDocumentAsync<SalesInvoice>(header.OriginalInvoice);
            if (invoice is not null)
            {
                if (header.Outlet == Guid.Empty && invoice.Outlet != Guid.Empty)
                    header.Outlet = invoice.Outlet;
                if (header.Contract == Guid.Empty && invoice.Contract != Guid.Empty)
                    header.Contract = invoice.Contract;
                if (header.Customer == Guid.Empty && invoice.Customer != Guid.Empty)
                    header.Customer = invoice.Customer;
            }
        }

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(SalesReturn document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

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
