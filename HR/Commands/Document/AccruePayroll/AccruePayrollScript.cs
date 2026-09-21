using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Команда «Начислить ФОТ» на утверждённом табеле: превращает отработанные часы в
// документ начисления. Формула — IPayrollCalculationService: оклад пропорционально
// часам периода или часы × HourlyRate.
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

        // НАЧИСЛЯЕМ РОВНО ОДИН РАЗ НА ТАБЕЛЬ. Кнопку можно нажать дважды, и без
        // этой отсечки второе нажатие создаёт ВТОРОЕ начисление — а оно по цепочке
        // порождает второй документ взносов, удваивая и обязательство перед фондом,
        // и удержание у сотрудника, и обе проводки в главной книге. Здесь триггер —
        // повторное действие пользователя, а не перезапуск цепочки проведения, но
        // класс ошибки и лекарство те же, что у PayrollAccrualEventHandler и
        // PurchaseOrderEventHandler.
        //
        // Ребро несёт только id концов, тип — у узла: сопоставляем одно с другим.
        // Ищется именно РЕБРО от этого табеля, а не любой родственник типа
        // «начисление» в графе.
        var family = await docs.GetDocumentFamilyAsync(sheet.MetaId);
        var accrualIds = new HashSet<Guid>(
            family.Nodes.Where(n => n.DocTypeMetaId == PayrollAccrualType).Select(n => n.DocId));
        foreach (var edge in family.Edges.Where(e => e.ParentDocId == sheet.MetaId && accrualIds.Contains(e.ChildDocId)))
        {
            var existing = await docs.GetDocumentAsync<PayrollAccrual>(edge.ChildDocId);
            if (existing != null && existing.Subtype != PayrollAccrual.Subtypes.Voided)
            {
                context.AddClientAction(ClientAction.Message("По этому табелю уже начислено."));
                return;
            }
        }

        if (sheet.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("В табеле нет строк."));
            return;
        }

        var employees = context.GetService<IDictionaryManager<Employee>>();
        var calc = context.GetService<IPayrollCalculationService>();
        var from = sheet.PeriodFrom == default ? DateTime.UtcNow.Date : sheet.PeriodFrom.Date;
        var to = sheet.PeriodTo == default ? from : sheet.PeriodTo.Date;

        var accrual = await docs.NewDocumentAsync<PayrollAccrual>("Draft", new Dictionary<string, object?>
        {
            ["Division"] = sheet.Division,
        });

        decimal total = 0m;
        var skipped = 0;
        foreach (var line in sheet.Lines)
        {
            var emp = await employees.GetRecordAsync(line.Employee);
            if (emp == null || emp.Position == Guid.Empty) { skipped++; continue; }

            var hours = line.Hours ?? 0m;
            var amount = await calc.AmountOfAsync(emp.Position, hours, from, to);
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
            .SetSubtypeAsync(PayrollAccrualType, accrual.MetaId, PayrollAccrual.Subtypes.Posted);
        await docs.AddLinkAsync(sheet.MetaId, accrual.MetaId);

        var note = skipped > 0 ? $" Пропущено строк: {skipped}." : "";
        context.AddClientAction(ClientAction.Message($"Начислено {total}." + note));
    }
}
