#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

public partial class BuildAbcPolicyCommand
{
    public override async Task ExecuteAsync(AbcProfile record, CommandContext context)
    {
        var n = await context.GetService<IAbcPolicy>().BuildAsync(record.MetaId, DateTime.UtcNow);
        context.AddClientAction(ClientAction.Message($"Предложений: {n}", "success"));
    }
}
