using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Commands;
using ZuloOne.Services.Contracts;

public partial class PlanPickWaveCommand
{
    public override async Task ExecuteAsync(
        IDictionary<string, object?> parameters, IList<ClientAction> clientActions)
    {
        var removed = await Context.GetService<ISalesFulfillmentService>().PlanPickWaveAsync();
        clientActions.Add(ClientAction.Message(removed == 0
            ? "Черновиков отбора с одной ячейки хранения, которые стоит собрать, нет."
            : $"Собрано в одно задание. Снято черновиков: {removed}."));
    }
}
