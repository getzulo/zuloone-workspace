using System;
using System.Linq;
using ZuloOne.Managers;

// Цеховая отметка: «операция отработана». Кнопка закрывает ОСТАВШИЕСЯ
// операции по НОРМЕ — наладка плюс машинное время, посчитанные при штамповке
// маршрута. Это ускоритель для цеха, где факт равен норме; расхождение
// вводят руками в строке (подтип Released не заперт).
//
// Признак «отмечено» — CompletedOn, а НЕ ActualMinutes: мгновенная операция с
// нулём минут законна, и по нулю её было бы не отличить от неотмеченной.
// Поэтому повтор кнопки не переписывает уже отмеченное — ни ручное время,
// ни исполнителя. Необязательные поля табличной части nullable (`decimal?`),
// в отличие от полей справочника, — отсюда `== null` и `?? 0`.
public partial class ConfirmOperationsCommand
{
    public override async Task ExecuteAsync(ProductionOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<ProductionOrder>(document.MetaId);
        if (full == null) return;

        if (full.Operations.Count == 0)
        {
            context.AddClientAction(ClientAction.Message(
                "У заказа нет операций: маршрут изделия не заведён или пуст."));
            return;
        }

        var on = full.DocumentDate == default ? DateTime.UtcNow.Date : full.DocumentDate.Date;
        var confirmed = 0;
        foreach (var op in full.Operations.Where(o => o.CompletedOn == null))
        {
            op.ActualMinutes = (op.SetupMinutes ?? 0) + (op.RunMinutes ?? 0m);
            op.CompletedOn = on;
            confirmed++;
        }

        if (confirmed == 0)
        {
            context.AddClientAction(ClientAction.Message("Все операции уже отмечены."));
            return;
        }

        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message(
            $"Отмечено операций: {confirmed}. Время проставлено по норме — поправьте факт в строке, если он другой."));
    }
}
