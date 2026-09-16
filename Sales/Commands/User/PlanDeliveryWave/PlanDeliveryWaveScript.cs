using System;
using System.Collections.Generic;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class PlanDeliveryWaveCommand
{
    public override async Task ExecuteAsync(
        IDictionary<string, object?> parameters, IList<ClientAction> clientActions)
    {
        var day = DateTime.UtcNow.Date;
        if (parameters.TryGetValue("Date", out var raw) && raw is DateTime specified)
            day = specified.Date;

        var created = await Context.GetService<IDeliveryService>().PlanWaveAsync(day);
        clientActions.Add(ClientAction.Message(created == 0
            ? "На эту дату живых расписаний без рейса нет."
            : $"Создано рейсов: {created}."));
    }
}
