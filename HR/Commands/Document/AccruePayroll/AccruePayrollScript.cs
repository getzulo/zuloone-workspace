using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

// Команда «Начислить ФОТ» на утверждённом табеле: превращает отработанные часы в
// документ начисления по ставке должности сотрудника (часы × HourlyRate).
//
// Почему расчёт здесь, а не в проводке табеля: ставка лежит в справочнике
// Position, а её чтение асинхронно — транзакционный скрипт синхронный и таких
// обращений сделать не может. Команда же async, поэтому источник (часы) и
// результат (деньги) разводятся правильно: табель хранит факт работы,
// начисление — сумму.
public partial class AccruePayrollCommand
{
    private static readonly Guid PayrollAccrualType = Guid.Parse("832edeee-5c1a-4f9b-8d3e-2a7c6f1d4b90");

    public override async Task ExecuteAsync(TimeSheet document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var sheet = await docs.GetDocumentAsync<TimeSheet>(document.MetaId);
        if (sheet == null) return;

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

            // Часы в строке генерируются nullable-свойством, ставка должности — нет.
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
        // Проведение начисления даёт движения по ФОТ и задолженности.
        await context.GetService<IDocumentPostingService>()
            .SetSubtypeAsync(PayrollAccrualType, accrual.MetaId, "Posted");
        await docs.AddLinkAsync(sheet.MetaId, accrual.MetaId);

        var note = skipped > 0 ? $" Пропущено строк: {skipped}." : "";
        context.AddClientAction(ClientAction.Message($"Начислено {total}." + note));
    }
}
