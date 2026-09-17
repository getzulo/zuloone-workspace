#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// One door for outlet contracts. Handlers and commands do not compare windows
// or read Receivable themselves — they ask here.
public partial class SalesContractService
{
    private readonly IDictionaryManager<SalesContract> _contracts;
    private readonly IDictionaryManager<CustomerOutlet> _outlets;
    private readonly IDictionaryManager<Customer> _customers;
    private readonly IDictionaryManager<LegalEntity> _legalEntities;
    private readonly ITotalsManager _totals;

    public SalesContractService(
        IDictionaryManager<SalesContract> contracts,
        IDictionaryManager<CustomerOutlet> outlets,
        IDictionaryManager<Customer> customers,
        IDictionaryManager<LegalEntity> legalEntities,
        ITotalsManager totals)
    {
        _contracts = contracts;
        _outlets = outlets;
        _customers = customers;
        _legalEntities = legalEntities;
        _totals = totals;
    }

    /// <summary>Active contract of the outlet on the date. Empty — none.</summary>
    public async Task<Guid> ResolveActiveAsync(Guid outletId, DateTime onDate)
    {
        if (outletId == Guid.Empty) return Guid.Empty;
        var day = onDate.Date;
        var match = (await _contracts.GetRecordsAsync($"Outlet = '{outletId}'"))
            .Where(c => !c.IsDisabled && Covers(c, day))
            .OrderByDescending(c => c.EffectiveFrom)
            .FirstOrDefault();
        return match?.MetaId ?? Guid.Empty;
    }

    /// <summary>Other live contract on the same outlet whose window overlaps.
    /// Empty — none. Disabled rows do not block a replacement.</summary>
    public async Task<Guid> FindOverlappingAsync(
        Guid outletId, Guid excludeId, DateTime from, DateTime? to)
    {
        if (outletId == Guid.Empty) return Guid.Empty;
        var clash = (await _contracts.GetRecordsAsync($"Outlet = '{outletId}'"))
            .FirstOrDefault(c => c.MetaId != excludeId
                && !c.IsDisabled
                && WindowsOverlap(from, to, c.EffectiveFrom, c.EffectiveTo));
        return clash?.MetaId ?? Guid.Empty;
    }

    /// <summary>Null — currency is compatible or there is nothing to check.</summary>
    public async Task<string?> CheckCurrencyAsync(Guid contractCurrency, Guid legalEntityId)
    {
        if (contractCurrency == Guid.Empty || legalEntityId == Guid.Empty)
            return null;
        var le = await _legalEntities.GetRecordAsync(legalEntityId);
        if (le is null || le.Currency == Guid.Empty)
            return null;
        if (le.Currency == contractCurrency)
            return null;
        return "Валюта договора должна совпадать с валютой юрлица продавца";
    }

    /// <summary>Hints to stamp on a header. Only filled keys should overwrite
    /// empty document fields. Contract is resolved from the outlet when empty.</summary>
    public async Task<Dictionary<string, object?>> ResolveStampAsync(
        Guid customerId, Guid outletId, Guid contractId, DateTime onDate)
    {
        var stamp = new Dictionary<string, object?>();
        if (outletId != Guid.Empty)
        {
            var outlet = await _outlets.GetRecordAsync(outletId);
            if (outlet is not null)
            {
                if (customerId == Guid.Empty)
                    stamp["Customer"] = outlet.Customer;
                if (outlet.Contact != Guid.Empty)
                    stamp["Contact"] = outlet.Contact;
                if (contractId == Guid.Empty)
                {
                    var resolved = await ResolveActiveAsync(outletId, onDate);
                    if (resolved != Guid.Empty)
                    {
                        contractId = resolved;
                        stamp["Contract"] = resolved;
                    }
                }
            }
        }

        if (contractId != Guid.Empty)
        {
            var contract = await _contracts.GetRecordAsync(contractId);
            if (contract is not null)
            {
                if (!stamp.ContainsKey("Customer") && customerId == Guid.Empty)
                    stamp["Customer"] = contract.Customer;
                if (outletId == Guid.Empty)
                    stamp["Outlet"] = contract.Outlet;
                if (contract.PaymentTerm != Guid.Empty)
                    stamp["PaymentTerm"] = contract.PaymentTerm;
                if (contract.DeliveryTerm != Guid.Empty)
                    stamp["DeliveryTerm"] = contract.DeliveryTerm;
                if (contract.LegalEntity != Guid.Empty)
                    stamp["LegalEntity"] = contract.LegalEntity;
            }
        }

        return stamp;
    }

    /// <summary>Null — the customer / outlet / contract triple is consistent
    /// on the date. Contract with the customer is required.</summary>
    public async Task<string?> ValidatePairAsync(
        Guid customerId, Guid outletId, Guid contractId, DateTime onDate)
    {
        if (contractId == Guid.Empty)
            return "Укажите договор с клиентом";

        CustomerOutlet? outlet = null;
        if (outletId != Guid.Empty)
        {
            outlet = await _outlets.GetRecordAsync(outletId);
            if (outlet is null)
                return "Торговая точка не найдена";
            if (outlet.IsDisabled)
                return "Торговая точка отключена";
            if (customerId != Guid.Empty && outlet.Customer != customerId)
                return "Точка принадлежит другому клиенту";
        }

        var contract = await _contracts.GetRecordAsync(contractId);
        if (contract is null)
            return "Договор не найден";
        if (contract.IsDisabled)
            return "Договор отключён";
        if (outletId != Guid.Empty && contract.Outlet != outletId)
            return "Договор принадлежит другой торговой точке";
        if (customerId != Guid.Empty && contract.Customer != customerId)
            return "Договор принадлежит другому клиенту";
        if (outlet is null && customerId != Guid.Empty && contract.Customer != customerId)
            return "Договор принадлежит другому клиенту";
        if (!Covers(contract, onDate.Date))
            return "Договор не действует на дату документа";

        return await CheckCurrencyAsync(contract.Currency, contract.LegalEntity);
    }

    /// <summary>Null — credit / prepaid / COD allows the document amount.</summary>
    public async Task<string?> CheckSettlementAsync(Guid customerId, Guid contractId, decimal documentAmount)
    {
        if (customerId == Guid.Empty)
            return "Укажите клиента";
        if (contractId == Guid.Empty)
            return "Укажите договор с клиентом";
        if (documentAmount <= 0m)
            return null;

        var contract = await _contracts.GetRecordAsync(contractId);
        if (contract is null)
            return "Договор не найден";

        var kind = contract.SettlementKind;
        if (kind == SettlementKind.Unspecified || kind == SettlementKind.CashOnDelivery)
            return null;

        if (kind == SettlementKind.Prepaid)
        {
            var prepaidOwed = await ReceivableOfAsync(customerId, contractId);
            if (prepaidOwed + documentAmount > 0m)
                return "По договору предоплата: сначала примите аванс, покрывающий сумму документа";
            return null;
        }

        var limit = contract.CreditLimit;
        var owed = await ReceivableOfAsync(customerId, limit > 0m ? contractId : Guid.Empty);
        if (limit <= 0m)
        {
            var customer = await _customers.GetRecordAsync(customerId);
            limit = customer?.CreditLimit ?? 0m;
        }
        if (limit <= 0m)
            return null;
        if (owed + documentAmount > limit)
            return $"Сумма {owed + documentAmount} превышает кредитный лимит {limit}";
        return null;
    }

    // Contract limit / prepaid — this slice. Customer-wide limit — all slices.
    private Task<decimal> ReceivableOfAsync(Guid customerId, Guid contractId)
    {
        var slice = new Dictionary<string, object?> { ["Customer"] = customerId };
        if (contractId != Guid.Empty)
            slice["SalesContract"] = contractId;
        return _totals.GetBalanceAsync("Receivable", "Amount", slice);
    }

    private static bool Covers(SalesContract c, DateTime day)
        => c.EffectiveFrom.Date <= day
        && day <= (c.EffectiveTo?.Date ?? DateTime.MaxValue);

    private static bool WindowsOverlap(DateTime aFrom, DateTime? aTo, DateTime bFrom, DateTime? bTo)
        => aFrom.Date <= (bTo?.Date ?? DateTime.MaxValue)
        && bFrom.Date <= (aTo?.Date ?? DateTime.MaxValue);
}
