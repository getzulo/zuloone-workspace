#nullable enable
namespace ZuloOne.Runtime.Generated;

// JournalEntry is the foundation of double-entry: a posting is accepted only when it
// balances. This guard makes «trial balance sums to zero» an invariant of the ledger
// rather than a report-time check.
//
// The header event receives the document WITHOUT its table-part rows loaded, so the
// lines are re-loaded through IDocumentManager (the sanctioned pattern for reading
// table parts in an event handler).
public partial class JournalEntryEventHandler : TypedDocumentEventHandler<JournalEntry>
{
    public override async Task<EventResult> OnBeforePostAsync(JournalEntry document, EventContext context)
    {
        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<JournalEntry>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Проводка без строк не проводится");

        decimal debit = 0m, credit = 0m;
        foreach (var line in lines)
        {
            if (line.Debit != 0m && line.Credit != 0m)
                return EventResult.Cancel("Строка не может быть одновременно дебетовой и кредитовой");
            debit += line.Debit;
            credit += line.Credit;
        }

        if (debit != credit)
            return EventResult.Cancel($"Проводка не сбалансирована: дебет {debit} ≠ кредит {credit}");

        return EventResult.Ok();
    }
}
