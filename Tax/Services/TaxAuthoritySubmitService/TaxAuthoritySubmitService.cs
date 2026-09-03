using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

// Мок канала в налоговый орган.
//
// Скриптам запрещены HttpClient, файлы и процессы — политика безопасности
// отклонит такой код при импорте. Поэтому «отправка» всегда принимается
// локально и возвращает квитанцию MOCK-OK. Подключение юрлица, если оно есть,
// только попадает в строку; его отсутствие не ошибка: стенд без госсистемы
// обязан проводить сдачу и оплату как прежде.
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

    /// <summary>Принять декларацию. Всегда успешно, даже без документа и без подключения.</summary>
    public Task<string> SubmitReturnAsync(Guid taxReturnId)
        => AcceptAsync("RETURN", taxReturnId, async () =>
        {
            var doc = await _documents.GetDocumentAsync<TaxReturn>(taxReturnId);
            return doc?.LegalEntity ?? Guid.Empty;
        });

    /// <summary>Принять оплату налога. Всегда успешно, даже без документа и без подключения.</summary>
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
            // Нет документа или справочника — мок всё равно принимает.
        }

        return receipt;
    }
}
