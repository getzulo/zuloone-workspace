using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Провести проводку»: баланс дебет=кредит и период на дату документа
// (IGeneralLedgerService). Непроводимый счёт (группа) — AccountCodeProblemAsync.
public partial class PostJournalEntryCommand
{
    public override async Task ExecuteAsync(JournalEntry document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<JournalEntry>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустую запись: добавьте строки."));
            return;
        }

        var gl = context.GetService<IGeneralLedgerService>();
        var date = full.DocumentDate == default ? DateTime.UtcNow.Date : full.DocumentDate.Date;
        if (await gl.ResolvePeriodAsync(date) is null)
        {
            context.AddClientAction(ClientAction.Message(
                $"Нет учётного периода на {date:yyyy-MM-dd} — проводка не проводится."));
            return;
        }

        var accounts = context.GetService<IDictionaryManager<ChartOfAccounts>>();
        decimal debit = 0m, credit = 0m;
        foreach (var line in full.Lines)
        {
            if (line.Debit != 0m && line.Credit != 0m)
            {
                context.AddClientAction(ClientAction.Message(
                    "Строка не может быть одновременно дебетовой и кредитовой."));
                return;
            }
            debit += line.Debit;
            credit += line.Credit;

            if (line.Account == Guid.Empty) continue;
            var acc = await accounts.GetRecordAsync(line.Account);
            if (acc == null) continue;
            var problem = await gl.AccountCodeProblemAsync(acc.Code);
            if (problem != null)
            {
                context.AddClientAction(ClientAction.Message(problem));
                return;
            }
        }

        if (debit != credit)
        {
            context.AddClientAction(ClientAction.Message(
                $"Проводка не сбалансирована: дебет {debit} ≠ кредит {credit}"));
            return;
        }

        full.Subtype = JournalEntry.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Проводка записана."));
    }
}
