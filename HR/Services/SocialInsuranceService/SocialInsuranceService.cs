using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Social insurance: contributions on the payroll fund. Rates and the wage ceiling
// are SETTINGS (HRSettings), not constants in code: each country has its own, and
// they change by law more often than the code. For KSA (GOSI) typical figures are
// 9.75% employee and 11.75% employer for nationals, 2% employer for foreigners,
// wage ceiling 45 000 SAR — but that is stand data, not service knowledge.
//
// The employee's nationality is compared to the employer's registration country
// (HRSettings.HomeCountry): "local" — full rate, "foreigner" — employer foreign
// rate only (the employee has no contribution).
public partial class SocialInsuranceService
{
    private readonly IDictionaryManager<HRSettings> _settings;
    private readonly IDictionaryManager<Employee> _employees;
    private readonly IDocumentManager _documents;
    private readonly IDocumentPostingService _posting;

    public SocialInsuranceService(
        IDictionaryManager<HRSettings> settings,
        IDictionaryManager<Employee> employees,
        IDocumentManager documents,
        IDocumentPostingService posting)
    {
        _settings = settings;
        _employees = employees;
        _documents = documents;
        _posting = posting;
    }

    /// <summary>SocialInsuranceAccrual document type — the target of the Posted transition.</summary>
    private static readonly Guid SocialInsuranceAccrualType = Guid.Parse("a0d03063-af77-4fd0-886b-223a9731f105");

    /// <summary>Contributions on one base: (employee, employer). Both zeros — the contour is not configured.</summary>
    public async Task<(decimal Employee, decimal Employer)> CalculateAsync(Guid employee, decimal grossAmount)
    {
        if (grossAmount <= 0m) return (0m, 0m);

        var s = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (s is null) return (0m, 0m);

        // Wage ceiling: the contribution is taken from the lesser of pay and ceiling.
        // Zero / missing ceiling = no ceiling, not "base is zero".
        var ceiling = s.SocialInsuranceWageCeiling;
        var contributoryBase = ceiling > 0m && grossAmount > ceiling ? ceiling : grossAmount;

        var isLocal = await IsLocalNationalAsync(employee, s);
        var scale = GlobalConstants.Get<int?>("AmountScale") ?? 2;

        if (!isLocal)
        {
            var foreignRate = s.SocialInsuranceForeignEmployerRate;
            return (0m, Math.Round(contributoryBase * foreignRate, scale, MidpointRounding.AwayFromZero));
        }

        return (
            Math.Round(contributoryBase * s.SocialInsuranceEmployeeRate, scale, MidpointRounding.AwayFromZero),
            Math.Round(contributoryBase * s.SocialInsuranceEmployerRate, scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>Contributory base after the ceiling — for display on the accrual line.</summary>
    public async Task<decimal> ContributoryBaseAsync(decimal grossAmount)
    {
        var s = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        var ceiling = s?.SocialInsuranceWageCeiling ?? 0m;
        return ceiling > 0m && grossAmount > ceiling ? ceiling : grossAmount;
    }

    /// <summary>
    /// Spawns a POSTED contribution accrual from "employee → accrued" pairs.
    /// null if the contour is not configured or contributions came out zero:
    /// social insurance is optional; without it payroll posts as before.
    /// </summary>
    public async Task<Guid?> CreateAccrualAsync(Guid division, IEnumerable<KeyValuePair<Guid, decimal>> gross)
    {
        var s = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (s is null) return null;

        var rows = new List<SocialInsuranceAccrualLinesTablePartRow>();
        foreach (var kv in gross)
        {
            var (employee, employer) = await CalculateAsync(kv.Key, kv.Value);
            if (employee == 0m && employer == 0m) continue;
            rows.Add(new SocialInsuranceAccrualLinesTablePartRow
            {
                Employee = kv.Key,
                ContributoryBase = await ContributoryBaseAsync(kv.Value),
                EmployeeContribution = employee,
                EmployerContribution = employer,
            });
        }
        if (rows.Count == 0) return null;

        var doc = await _documents.NewDocumentAsync<SocialInsuranceAccrual>("Draft",
            new Dictionary<string, object?> { ["Division"] = division });
        foreach (var r in rows) doc.Lines.Add(r);

        // Save as a DRAFT and only then transition: the Posted subtype is locked
        // (isReadOnly), and lines written already in it will be rejected by the guard.
        await _documents.SaveDocumentAsync(doc);
        await _posting.SetSubtypeAsync(SocialInsuranceAccrualType, doc.MetaId, "Posted");
        return doc.MetaId;
    }

    /// <summary>National of the employer's registration country? Missing data is treated as "local".</summary>
    private async Task<bool> IsLocalNationalAsync(Guid employee, HRSettings settings)
    {
        var home = settings.HomeCountry;
        if (home == Guid.Empty) return true;

        var e = await _employees.GetRecordAsync(employee);
        var nationality = e?.Nationality ?? Guid.Empty;
        // Nationality not filled — not a reason to deny the employee contributions:
        // treat as local, not foreign. Otherwise an empty field would silently cut
        // the deductions.
        return nationality == Guid.Empty || nationality == home;
    }
}
