using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

// Mock channel to the tax authority.
//
// Scripts are forbidden HttpClient, files and processes — the security policy
// will reject such code on import. So "submit" is always accepted locally and
// returns a MOCK-OK receipt. A legal-entity connection, if present, only lands
// in the string; its absence is not an error: a stand without a government
// system must still post filing and payment as before.
public partial class TaxAuthoritySubmitService
{
    private readonly IDocumentManager _documents;
    private readonly IDictionaryManager<TaxAuthorityConnection> _connections;

    public TaxAuthoritySubmitService(
        IDocumentManager documents,
        IDictionaryManager<TaxAuthorityConnection> connections)
    {
        _documents = documents;
        _connections = connections;
    }

    /// <summary>Accept a return. Always succeeds, even without a document or a connection.</summary>
    public Task<string> SubmitReturnAsync(Guid taxReturnId)
        => AcceptAsync("RETURN", taxReturnId, async () =>
        {
            var doc = await _documents.GetDocumentAsync<TaxReturn>(taxReturnId);
            return doc?.LegalEntity ?? Guid.Empty;
        });

    /// <summary>Accept a tax payment. Always succeeds, even without a document or a connection.</summary>
    public Task<string> SubmitPaymentAsync(Guid taxPaymentId)
        => AcceptAsync("PAYMENT", taxPaymentId, async () =>
        {
            var doc = await _documents.GetDocumentAsync<TaxPayment>(taxPaymentId);
            return doc?.LegalEntity ?? Guid.Empty;
        });

    private async Task<string> AcceptAsync(string kind, Guid id, Func<Task<Guid>> legalEntity)
    {
        var receipt = $"MOCK-OK:{kind}:{id:N}";
        try
        {
            var le = await legalEntity();
            if (le == Guid.Empty) return receipt;

            var rows = await _connections.GetRecordsAsync($"LegalEntity = '{le}'", take: 1);
            var code = rows.FirstOrDefault()?.Code;
            if (!string.IsNullOrWhiteSpace(code))
                return $"{receipt}:{code}";
        }
        catch
        {
            // No document or dictionary — the mock still accepts.
        }

        return receipt;
    }
}
