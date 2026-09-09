#nullable enable
namespace ZuloOne.Runtime.Generated;

// Ensures at most one primary contact per customer.
// When IsPrimary is set to true, all OTHER contacts of the same customer
// that were previously primary are set to false before this record is saved.
public partial class CustomerContactEventHandler : TypedDictionaryEventHandler<CustomerContact>
{
    public override async Task<EventResult> OnBeforeSaveAsync(CustomerContact record, bool isNew, EventContext context)
    {
        if (!record.IsPrimary)
            return EventResult.Ok();

        if (record.Customer == Guid.Empty)
            return EventResult.Ok();

        // Clear IsPrimary on other contacts of this customer.
        var dm = context.GetService<IDictionaryManager<CustomerContact>>();
        var others = (await dm.GetRecordsAsync($"Customer = '{record.Customer}'"))
            .Where(c => c.IsPrimary);
        foreach (var other in others)
        {
            // For new records MetaId is unstable; but we still clear everyone —
            // after save this record will be the only primary one.
            other.IsPrimary = false;
            await dm.SaveRecordAsync(other);
        }

        return EventResult.Ok();
    }
}
