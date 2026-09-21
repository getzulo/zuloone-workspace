#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

public partial class PayrollVoidService
{
    private readonly IDocumentManager _docs;
    private readonly ITotalsManager _totals;
    private readonly IDocumentPostingService _posting;

    public PayrollVoidService(
        IDocumentManager docs,
        ITotalsManager totals,
        IDocumentPostingService posting)
    {
        _docs = docs;
        _totals = totals;
        _posting = posting;
    }

    private static readonly Guid PayrollAccrualType = Guid.Parse("832edeee-5c1a-4f9b-8d3e-2a7c6f1d4b90");
    private static readonly Guid PayrollPaymentType = Guid.Parse("50fcf37d-6a2c-4e1b-9d3f-7c4a6e2d1b85");
    private static readonly Guid SocialInsuranceAccrualType = Guid.Parse("a0d03063-af77-4fd0-886b-223a9731f105");
    private static readonly Guid SocialInsurancePaymentType = Guid.Parse("aa4abe6c-0f27-42f0-9284-5f084e6b7274");

    public async Task<string?> VoidAccrualAsync(Guid accrualId)
    {
        var accrual = await _docs.GetDocumentAsync<PayrollAccrual>(accrualId);
        if (accrual == null) return "Начисление не найдено.";
        if (accrual.Subtype != PayrollAccrual.Subtypes.Posted)
            return "Аннулировать можно только проведённое начисление.";

        var family = await _docs.GetDocumentFamilyAsync(accrualId);
        var siIds = new HashSet<Guid>(
            family.Nodes.Where(n => n.DocTypeMetaId == SocialInsuranceAccrualType).Select(n => n.DocId));
        var siChildren = family.Edges
            .Where(e => e.ParentDocId == accrualId && siIds.Contains(e.ChildDocId))
            .Select(e => e.ChildDocId)
            .ToList();

        var withhold = new Dictionary<Guid, decimal>();
        foreach (var siId in siChildren)
        {
            var si = await _docs.GetDocumentAsync<SocialInsuranceAccrual>(siId);
            if (si == null || si.Subtype == SocialInsuranceAccrual.Subtypes.Voided) continue;
            if (si.Subtype != SocialInsuranceAccrual.Subtypes.Posted)
                return "Связанное начисление соцстраха ещё черновик — проведите или удалите его.";
            foreach (var line in si.Lines)
                withhold[line.Employee] = (withhold.TryGetValue(line.Employee, out var v) ? v : 0m)
                    + line.EmployeeContribution;
        }

        foreach (var line in accrual.Lines)
        {
            var liab = await _totals.GetBalanceAsync("PayrollLiability", "Amount",
                new Dictionary<string, object?> { ["Employee"] = line.Employee });
            var taken = withhold.TryGetValue(line.Employee, out var w) ? w : 0m;
            if (liab + 0.0001m < line.Amount - taken)
                return "Нельзя аннулировать: задолженность уже погашена выплатой.";
        }

        foreach (var siId in siChildren)
        {
            var err = await VoidSocialInsuranceAccrualAsync(siId);
            if (err != null) return err;
        }

        await _posting.SetSubtypeAsync(PayrollAccrualType, accrualId, PayrollAccrual.Subtypes.Voided);
        return null;
    }

    public async Task<string?> VoidPaymentAsync(Guid paymentId)
    {
        var pay = await _docs.GetDocumentAsync<PayrollPayment>(paymentId);
        if (pay == null) return "Выплата не найдена.";
        if (pay.Subtype != PayrollPayment.Subtypes.Paid)
            return "Аннулировать можно только проведённую выплату.";

        await _posting.SetSubtypeAsync(PayrollPaymentType, paymentId, PayrollPayment.Subtypes.Voided);
        return null;
    }

    public async Task<string?> VoidSocialInsuranceAccrualAsync(Guid siId)
    {
        var si = await _docs.GetDocumentAsync<SocialInsuranceAccrual>(siId);
        if (si == null) return "Начисление соцстраха не найдено.";
        if (si.Subtype == SocialInsuranceAccrual.Subtypes.Voided) return null;
        if (si.Subtype != SocialInsuranceAccrual.Subtypes.Posted)
            return "Аннулировать можно только проведённые взносы.";

        foreach (var line in si.Lines)
        {
            var slice = new Dictionary<string, object?>
            {
                ["Employee"] = line.Employee,
                ["Division"] = si.Division,
            };
            var emp = await _totals.GetBalanceAsync("SocialInsurance", "EmployeeContribution", slice);
            var emplr = await _totals.GetBalanceAsync("SocialInsurance", "EmployerContribution", slice);
            if (emp + 0.0001m < line.EmployeeContribution || emplr + 0.0001m < line.EmployerContribution)
                return "Нельзя аннулировать взносы: они уже уплачены в фонд.";
        }

        await _posting.SetSubtypeAsync(SocialInsuranceAccrualType, siId, SocialInsuranceAccrual.Subtypes.Voided);
        return null;
    }

    public async Task<string?> VoidSocialInsurancePaymentAsync(Guid paymentId)
    {
        var pay = await _docs.GetDocumentAsync<SocialInsurancePayment>(paymentId);
        if (pay == null) return "Платёж в фонд не найден.";
        if (pay.Subtype != SocialInsurancePayment.Subtypes.Paid)
            return "Аннулировать можно только проведённый платёж в фонд.";

        await _posting.SetSubtypeAsync(SocialInsurancePaymentType, paymentId, SocialInsurancePayment.Subtypes.Voided);
        return null;
    }
}
