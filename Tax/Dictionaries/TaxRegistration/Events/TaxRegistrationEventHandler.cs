#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Dated tax number of a party. Overlapping windows of the same
// party/tax/jurisdiction are refused here (input), while TaxService still
// refuses to pick when two rows match — same split as TaxRate.
public partial class TaxRegistrationEventHandler : TypedDictionaryEventHandler<TaxRegistration>
{
    public override async Task<EventResult> OnBeforeSaveAsync(
        TaxRegistration record, bool isNew, EventContext context)
    {
        var type = NormalizePartyType(record.PartyType);
        if (type is null)
            return EventResult.Cancel(
                "Тип стороны: LegalEntity, Customer или Supplier");
        record.PartyType = type;

        if (record.PartyId == Guid.Empty)
            return EventResult.Cancel("Укажите сторону регистрации");
        if (record.Tax == Guid.Empty)
            return EventResult.Cancel("Укажите налог регистрации");
        if (record.Jurisdiction == Guid.Empty)
            return EventResult.Cancel("Укажите юрисдикцию регистрации");
        if (string.IsNullOrWhiteSpace(record.RegistrationNumber))
            return EventResult.Cancel("Укажите регистрационный номер");
        if (record.ValidTo.HasValue && record.ValidFrom > record.ValidTo.Value)
            return EventResult.Cancel(
                "Окно регистрации задано наоборот: дата начала позже даты окончания");

        var overlap = await context.GetService<ITaxService>()
            .FindOverlappingRegistrationAsync(
                record.PartyType, record.PartyId, record.Tax, record.Jurisdiction,
                isNew ? Guid.Empty : record.MetaId,
                record.ValidFrom, record.ValidTo);
        if (overlap is not null)
            return EventResult.Cancel(
                $"На этот период уже есть регистрация {overlap.RegistrationNumber} " +
                $"с {overlap.ValidFrom:yyyy-MM-dd}");

        return EventResult.Ok();
    }

    private static string? NormalizePartyType(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Equals("LegalEntity", StringComparison.OrdinalIgnoreCase))
            return "LegalEntity";
        if (value.Equals("Customer", StringComparison.OrdinalIgnoreCase))
            return "Customer";
        if (value.Equals("Supplier", StringComparison.OrdinalIgnoreCase))
            return "Supplier";
        return null;
    }
}
