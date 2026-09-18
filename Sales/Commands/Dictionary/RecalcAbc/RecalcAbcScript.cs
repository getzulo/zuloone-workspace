#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

public partial class RecalcAbcCommand
{
    public override async Task ExecuteAsync(AbcProfile record, CommandContext context)
    {
        var n = await context.GetService<IAbcClassifier>().RecalcAsync(record.MetaId, DateTime.UtcNow);
        context.AddClientAction(ClientAction.Message($"Пересчитано записей: {n}", "success"));
    }
}
