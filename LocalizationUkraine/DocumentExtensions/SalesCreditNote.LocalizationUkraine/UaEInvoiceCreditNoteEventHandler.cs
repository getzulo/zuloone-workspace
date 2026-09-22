#nullable enable
using System.Threading.Tasks;
using ZuloOne.Runtime.Events;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Проведена кредит-нота → розрахунок коригування на реєстрацію в ЄРПН.
//
// Та сама ланка, що й накладна на рахунку: після next(), бо коригування
// складається з уже сторнованої бази. Без посилання на початковий рахунок
// сервіс сам відмовиться — тут другої копії цієї перевірки немає.
[ExtensionOf("SalesCreditNote")]
public partial class UaEInvoiceCreditNoteEventHandler : TypedDocumentEventHandler<SalesCreditNote>
{
    public override async Task<EventResult> OnAfterPostAsync(
        SalesCreditNote document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != SalesCreditNote.Subtypes.Posted) return EventResult.Ok();

        await context.GetService<IUaEInvoice>().EnsureForCreditNoteAsync(document.MetaId);
        return EventResult.Ok();
    }
}
