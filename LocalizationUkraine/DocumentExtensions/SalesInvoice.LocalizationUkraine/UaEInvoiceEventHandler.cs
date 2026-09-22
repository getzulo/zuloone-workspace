#nullable enable
using System.Threading.Tasks;
using ZuloOne.Runtime.Events;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Виписаний рахунок → податкова накладна на реєстрацію в ЄРПН.
//
// ЧОМУ ПІСЛЯ next() І ЧОМУ САМЕ НА Issued. Накладна складається за ПЕРШОЮ
// ПОДІЄЮ, і на момент виписки рахунку ця подія вже сталася — податкові
// зобов'язання нарахував драйвер, який відпрацював у ланцюжку нижче. Робота
// після next() бачить регістри вже заповненими.
//
// Ланка 30 — після власника документа й після української проводки ПДВ.
// Порядок має значення тільки в один бік: накладна не повинна випереджати
// нарахування, бо тоді в неї нічого класти.
[ExtensionOf("SalesRealization")]
public partial class UaEInvoiceEventHandler : TypedDocumentEventHandler<SalesRealization>
{
    public override async Task<EventResult> OnAfterPostAsync(
        SalesRealization document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != SalesRealization.Subtypes.Issued) return EventResult.Ok();

        // Сервіс сам вирішує, чи контур увімкнений і чи виписує це юрособа
        // накладні взагалі: тут не місце для другої копії тих самих умов.
        await context.GetService<IUaEInvoice>().EnsureForInvoiceAsync(document.MetaId);
        return EventResult.Ok();
    }
}
