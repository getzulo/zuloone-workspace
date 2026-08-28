// Команда «Развернуть спецификацию» на подтипе Draft производственного заказа:
// заполняет табличную часть Components потребностью из BOM под заданное
// количество изделия. Раньше строки заказа набивались руками, хотя спецификация
// уже описана в BillOfMaterials/BomComponent.
//
// Разворачивание BOM живёт в BomService — команда тонкая: проверить → развернуть
// → записать. Скрипт лежит в ТОЙ ЖЕ модели, что и сервис; если контракт своей
// модели окажется недоступен на момент компиляции, логику надо будет позвать
// иначе — это выясняет компиляция.
public partial class ExpandBomCommand
{
    public override async Task ExecuteAsync(ProductionOrder document, CommandContext context)
    {
        if (document.Product == Guid.Empty || document.Quantity <= 0m)
        {
            context.AddClientAction(ClientAction.Message("Укажите изделие и количество больше нуля."));
            return;
        }

        var bom = context.GetService<IBomService>();
        var need = await bom.ExpandByProductAsync(document.Product, document.Quantity);
        if (need.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Для изделия не найдена спецификация."));
            return;
        }

        // Документ перечитывается целиком: у заголовка из команды табличная часть пуста.
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<ProductionOrder>(document.MetaId);
        if (full == null) return;

        // Разворачивание ЗАМЕЩАЕТ строки: команда — источник истины по потребности.
        full.Components.Clear();
        foreach (var kv in need)
        {
            full.Components.Add(new ProductionOrderComponentsTablePartRow
            {
                Component = kv.Key,
                QtyRequired = kv.Value,
            });
        }

        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message($"Спецификация развёрнута: строк — {need.Count}."));
    }
}
