#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

public partial class CreateAbcPurchaseDraftsCommand
{
    public override async Task ExecuteAsync(AbcProfile record, CommandContext context)
    {
        var ids = await context.GetService<IAbcPolicy>().CreatePurchaseDraftsAsync(record.MetaId, DateTime.UtcNow);
        var text = ids.Count == 0
            ? "Новых черновиков нет. К заказу пусто, по товару нет принятого прихода, или черновик этого профиля уже стоит."
            : $"Черновиков: {ids.Count}. Поставщик, ячейка и цена — с последнего принятого прихода. Ожидаемый приход пустой.";
        context.AddClientAction(ClientAction.Message(text, ids.Count == 0 ? "warning" : "success"));
    }
}
