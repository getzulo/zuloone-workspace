using System;
#nullable enable
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class SalesContractEventHandler : TypedDictionaryEventHandler<SalesContract>
{
    public override async Task<EventResult> OnBeforeCreateAsync(SalesContract record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("SalesContract");
        if (record.Currency == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "Currency");
            if (createId != Guid.Empty) record.Currency = createId;
        }
        if (record.EffectiveFrom.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "EffectiveFrom");
            if (createDay.Year >= 1902) record.EffectiveFrom = createDay;
        }
        if (record.LegalEntity == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "LegalEntity");
            if (createId != Guid.Empty) record.LegalEntity = createId;
        }
        if (record.PaymentTerm == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "PaymentTerm");
            if (createId != Guid.Empty) record.PaymentTerm = createId;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(SalesContract record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Outlet == Guid.Empty)
            return EventResult.Cancel("Укажите торговую точку договора");
        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Укажите наименование договора");
        if (record.Currency == Guid.Empty)
            return EventResult.Cancel("Укажите валюту договора");
        if (record.SettlementKind == SettlementKind.Unspecified)
            return EventResult.Cancel("Укажите тип расчёта");
        if (record.EffectiveFrom == default)
            return EventResult.Cancel("Укажите дату начала действия договора");
        if (record.EffectiveTo.HasValue && record.EffectiveFrom.Date > record.EffectiveTo.Value.Date)
            return EventResult.Cancel("Окно действия задано наоборот: дата начала позже даты окончания");

        var outlets = context.GetService<IDictionaryManager<CustomerOutlet>>();
        var outlet = await outlets.GetRecordAsync(record.Outlet);
        if (outlet is null)
            return EventResult.Cancel("Торговая точка не найдена");
        if (outlet.IsDisabled)
            return EventResult.Cancel("Нельзя заключить договор на отключённую точку");

        record.Customer = outlet.Customer;

        var contracts = context.GetService<ISalesContractService>();
        var currencyError = await contracts.CheckCurrencyAsync(record.Currency, record.LegalEntity);
        if (currencyError != null)
            return EventResult.Cancel(currencyError);

        var clash = await contracts.FindOverlappingAsync(
            record.Outlet, record.MetaId, record.EffectiveFrom, record.EffectiveTo);
        if (clash != Guid.Empty)
        {
            var other = await context.GetService<IDictionaryManager<SalesContract>>().GetRecordAsync(clash);
            return EventResult.Cancel(
                $"Окно действия пересекается с договором «{other?.Name ?? clash.ToString()}» "
                + $"({Window(other?.EffectiveFrom ?? default, other?.EffectiveTo)}). "
                + "У точки на каждую дату должен действовать ровно один договор.");
        }

        return EventResult.Ok();
    }

    private static string Window(DateTime from, DateTime? to)
        => to.HasValue ? $"{from:yyyy-MM-dd} — {to.Value:yyyy-MM-dd}" : $"с {from:yyyy-MM-dd}";
}
