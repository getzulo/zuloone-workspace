using System;
using System.Collections.Generic;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

public partial class CompleteTripCommand
{
    public override async Task ExecuteAsync(DeliveryTrip document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<DeliveryTrip>(document.MetaId);
        if (full == null) return;

        var delivery = context.GetService<IDeliveryService>();
        var reason = await delivery.ValidateTripAsync(full.MetaId);
        if (reason != null)
        {
            context.AddClientAction(ClientAction.Message(reason));
            return;
        }

        // Промахи по окнам считаются ДО перевода в Completed: подтип заперт, и
        // после перехода строки уже не перечитать на правку.
        var late = await delivery.LateStopsSummaryAsync(full.MetaId);
        var pins = await delivery.PinOffSummaryAsync(full.MetaId);

        // Stamp before the read-only Completed subtype; a later header write is refused.
        full.ActualComplete = DateTime.UtcNow;
        full.Subtype = DeliveryTrip.Subtypes.Completed;
        await docs.SaveDocumentAsync(full);

        // Завершение не молчит про опоздания: диспетчеру они нужны сейчас, а не
        // в отчёте через месяц. Рейс при этом завершается в любом случае —
        // опоздание это ФАКТ, а не ошибка ввода.
        var notes = new List<string>();
        if (late.Length > 0) notes.Add($"Вне окна доставки: {late}");
        if (pins.Length > 0) notes.Add($"Вдали от точки: {pins}");
        context.AddClientAction(ClientAction.Message(
            notes.Count == 0
                ? "Рейс завершён."
                : "Рейс завершён. " + string.Join(". ", notes) + "."));
    }
}
