#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Inventory моделью GLIntegration: ОТПУСК со склада попадает в
// главную книгу — Dr списание запасов / Cr запасы. Та же логика, что у
// корректировки остатков, и потому тот же сервис: обработчик решает только КОГДА.
//
// Отпуск — это выбытие мимо продажи, поэтому счёт списания, а не COGS: иначе
// внутреннее перемещение ценностей исказило бы валовую маржу.
public partial class GoodsIssueGLEventHandler : TypedDocumentEventHandler<GoodsIssue>
{
    public override async Task<EventResult> OnAfterPostAsync(GoodsIssue document, EventContext context)
    {
        if (document.Subtype != "Posted") return EventResult.Ok();

        try
        {
            var jeId = await context.GetService<IInventoryWriteOffGLService>()
                .PostAsync(document.MetaId, document.FromCell, document.DocumentDate,
                           "Goods issue " + document.MetaId);
            if (jeId.HasValue)
                await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);
        }
        catch
        {
            // Разноска GL зависит от настройки счетов и не должна ронять отпуск.
        }

        return EventResult.Ok();
    }
}
