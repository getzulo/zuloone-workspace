using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;

// Mock channel to the tax authority.
//
// Scripts are forbidden HttpClient, files and processes — the security policy
// will reject such code on import. Each attempt writes an append-only
// TaxSubmission row. Managers are resolved at call time: this service is often
// constructed inside a short-lived ScriptServices scope that is already
// disposed when Submit* runs.
public partial class TaxAuthoritySubmitService
{
    /// <summary>File a return. Writes a journal row; rejects only when the connection asks the mock to.</summary>
    public Task<string> SubmitReturnAsync(Guid taxReturnId)
        => SubmitAsync("RETURN", taxReturnId, async () =>
        {
            var doc = await ScriptServices.Get<IDocumentManager>().GetDocumentAsync<TaxReturn>(taxReturnId);
            return doc?.LegalEntity ?? Guid.Empty;
        });

    /// <summary>File a tax payment. Same journal, same mock knob.</summary>
    public Task<string> SubmitPaymentAsync(Guid taxPaymentId)
        => SubmitAsync("PAYMENT", taxPaymentId, async () =>
        {
            var doc = await ScriptServices.Get<IDocumentManager>().GetDocumentAsync<TaxPayment>(taxPaymentId);
            return doc?.LegalEntity ?? Guid.Empty;
        });

    private async Task<string> SubmitAsync(string kind, Guid sourceId, Func<Task<Guid>> legalEntity)
    {
        var le = Guid.Empty;
        try { le = await legalEntity(); }
        catch { }

        var dict = ScriptServices.Get<IDictionaryManager>();
        TaxAuthorityConnection? conn = null;
        if (le != Guid.Empty)
        {
            var rows = await dict.GetRecordsAsync<TaxAuthorityConnection>($"LegalEntity = '{le}'", take: 1);
            conn = rows.FirstOrDefault();
        }

        var reject = conn != null && conn.IsMockReject;
        var receipt = reject
            ? $"MOCK-REJECT:{kind}:{sourceId:N}"
            : $"MOCK-OK:{kind}:{sourceId:N}";
        if (!string.IsNullOrWhiteSpace(conn?.Code))
            receipt += $":{conn.Code}";

        var row = dict.NewRecord<TaxSubmission>();
        row.Kind = kind;
        row.SourceId = sourceId;
        row.LegalEntity = le;
        row.Authority = conn?.Authority ?? Guid.Empty;
        row.ConnectionCode = conn?.Code ?? string.Empty;
        row.Environment = conn == null ? string.Empty : conn.Environment.ToString();
        row.SubmittedAt = DateTime.UtcNow;
        row.Status = reject ? "Rejected" : "Accepted";
        row.Receipt = receipt;
        row.ResponseMessage = reject
            ? "Мок налогового органа отклонил сдачу (IsMockReject)."
            : "Мок налогового органа принял сдачу.";
        await dict.SaveRecordAsync(row);

        return receipt;
    }
}
