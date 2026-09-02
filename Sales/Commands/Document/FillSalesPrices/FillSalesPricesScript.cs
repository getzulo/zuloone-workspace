using System.Linq;

// Команда «Заполнить цены» на черновике счёта: проставляет UnitPrice из прайса
// клиента, а где его нет — из умолчания карточки товара.
//
// Почему командой, а не автоподстановкой при вводе строки: построчного хука в
// платформе нет — события справочника/документа приходят на ШАПКУ и строк не
// видят, а SaveDocumentAsync во время проведения переписывает все строки и в
// проводках запрещён. Команда — единственное место, где можно пройти строки и
// сохранить документ целиком.
//
// Заполняются ТОЛЬКО пустые цены. Цена, введённая руками, — это решение
// человека (согласованная скидка, спорная позиция), и затирать его подбором
// нельзя. Кому нужно переподобрать — очистит цену и нажмёт снова.
public partial class FillSalesPricesCommand
{
    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var pricing = context.GetService<IPricingService>();

        // Строки у заголовка из команды пусты — документ перечитывается.
        var full = await docs.GetDocumentAsync<SalesInvoice>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("В документе нет строк."));
            return;
        }

        var filled = 0;
        var missing = 0;
        foreach (var line in full.Lines.Where(l => l.UnitPrice <= 0m))
        {
            // Дата документа, а не сегодня: перевыставляя мартовский счёт в мае,
            // мы обязаны взять мартовскую цену.
            var price = await pricing.ResolveSalePriceAsync(
                line.Item, line.Unit, full.Customer, full.DocumentDate);

            if (price == null) { missing++; continue; }
            line.UnitPrice = price.Value;
            filled++;
        }

        if (filled == 0)
        {
            context.AddClientAction(ClientAction.Message(missing > 0
                ? $"Цены не найдены ни для одной из {missing} пустых строк."
                : "Все строки уже с ценой."));
            return;
        }

        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message(missing == 0
            ? $"Заполнено цен: {filled}."
            : $"Заполнено цен: {filled}; не найдено: {missing}."));
    }
}
