using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

// "Accrue payroll" command on an approved timesheet: turns hours worked into an
// accrual document at the employee's position rate (hours × HourlyRate).
//
// Why the calculation lives here, not in the timesheet posting: the rate is on
// the Position dictionary and reading it is async — a transactional script is
// synchronous and cannot make those calls. The command is async, so source
// (hours) and result (money) stay separated correctly: the timesheet stores the
// work fact, the accrual stores the amount.
public partial class AccruePayrollCommand
{
    private static readonly Guid PayrollAccrualType = Guid.Parse("832edeee-5c1a-4f9b-8d3e-2a7c6f1d4b90");

    public override async Task ExecuteAsync(TimeSheet document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var sheet = await docs.GetDocumentAsync<TimeSheet>(document.MetaId);
        if (sheet == null) return;

        // ACCRUE EXACTLY ONCE PER TIMESHEET. The button can be pressed twice, and
        // without this guard the second press creates a SECOND accrual — which then
        // spawns a second contribution document, doubling the liability to the fund,
        // the employee withholding, and both GL postings. The trigger here is a
        // repeated user action, not a posting-chain restart, but the class of error
        // and the remedy are the same as in PayrollAccrualEventHandler and
        // PurchaseOrderEventHandler.
        //
        // An edge carries only endpoint ids; the type lives on the node — we match
        // one against the other. We look for an EDGE from this timesheet, not any
        // "accrual" relative in the graph.
        var family = await docs.GetDocumentFamilyAsync(sheet.MetaId);
        var accrualIds = new HashSet<Guid>(
            family.Nodes.Where(n => n.DocTypeMetaId == PayrollAccrualType).Select(n => n.DocId));
        if (family.Edges.Any(e => e.ParentDocId == sheet.MetaId && accrualIds.Contains(e.ChildDocId)))
        {
            context.AddClientAction(ClientAction.Message("По этому табелю уже начислено."));
            return;
        }

        if (sheet.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("В табеле нет строк."));
            return;
        }

        var employees = context.GetService<IDictionaryManager<Employee>>();
        var positions = context.GetService<IDictionaryManager<Position>>();

        var accrual = await docs.NewDocumentAsync<PayrollAccrual>("Draft", new Dictionary<string, object?>
        {
            ["Division"] = sheet.Division,
        });

        decimal total = 0m;
        var skipped = 0;
        foreach (var line in sheet.Lines)
        {
            var emp = await employees.GetRecordAsync(line.Employee);
            var pos = emp == null ? null : await positions.GetRecordAsync(emp.Position);
            if (pos == null) { skipped++; continue; }

            // Hours on the line are generated as a nullable property; the position rate is not.
            var hours = line.Hours ?? 0m;
            var amount = Math.Round(hours * pos.HourlyRate, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0m) { skipped++; continue; }

            accrual.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = line.Employee, Amount = amount });
            total += amount;
        }

        if (accrual.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Начислять нечего: у сотрудников не найдена должность со ставкой."));
            return;
        }

        await docs.SaveDocumentAsync(accrual);
        // Posting the accrual writes payroll and liability movements.
        await context.GetService<IDocumentPostingService>()
            .SetSubtypeAsync(PayrollAccrualType, accrual.MetaId, PayrollAccrual.Subtypes.Posted);
        await docs.AddLinkAsync(sheet.MetaId, accrual.MetaId);

        var note = skipped > 0 ? $" Пропущено строк: {skipped}." : "";
        context.AddClientAction(ClientAction.Message($"Начислено {total}." + note));
    }
}
