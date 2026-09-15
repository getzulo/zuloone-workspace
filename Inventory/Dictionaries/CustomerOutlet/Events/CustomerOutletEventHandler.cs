#nullable enable
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class CustomerOutletEventHandler : TypedDictionaryEventHandler<CustomerOutlet>
{
    public override async Task<EventResult> OnBeforeSaveAsync(CustomerOutlet record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Customer == Guid.Empty)
            return EventResult.Cancel("Укажите клиента торговой точки");
        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Укажите наименование торговой точки");

        if (record.Contact != Guid.Empty)
        {
            var contact = await context.GetService<IDictionaryManager<CustomerContact>>()
                .GetRecordAsync(record.Contact);
            if (contact is not null && contact.Customer != record.Customer)
                return EventResult.Cancel("Контакт должен принадлежать тому же клиенту, что и точка");
        }

        return EventResult.Ok();
    }
}
