#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;

// Calendar due date. PaymentTerm.Days is the only rule; no EOM / next-10th.
// Resolves the dictionary at call time (same as TradeProfileService): a
// constructor-captured manager dies before the caller's save finishes.
public partial class PaymentDueService
{
    /// <summary>Days on the term, or 0 when the id is empty / the row is missing.</summary>
    public async Task<int> DaysOfAsync(Guid termId)
    {
        if (termId == Guid.Empty) return 0;
        var row = await ScriptServices.Get<IDictionaryManager<PaymentTerm>>().GetRecordAsync(termId);
        return row?.Days ?? 0;
    }

    /// <summary>Start date plus days. Empty clock (year &lt; 1902) uses today.
    /// Negative days count as zero — due on the start date.</summary>
    public DateTime DueOn(DateTime start, int days)
    {
        var day = start.Year < 1902 ? DateTime.UtcNow.Date : start.Date;
        return day.AddDays(days < 0 ? 0 : days);
    }
}
