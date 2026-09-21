using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Кнопка «Вивантажити для подання» на декларации.
//
// Команда, а не задание: выгрузка нужна тогда, когда бухгалтер собрался
// подавать, и ровно по той декларации, которую он смотрит. Стоит на ОБОИХ
// подтипах — и на черновике, и на сданной: сданную переподают и сверяют чаще,
// чем черновик.
//
// Тип декларации берётся из настроек пакета, а не из кнопки: он же решает, по
// какому набору ячеек (TaxReportMapping) собиралась декларация, и разъехаться
// эти два места не должны.
public partial class UaExportTaxReturnCommand
{
    public override async Task ExecuteAsync(TaxReturn document, CommandContext context)
    {
        var settings = (await context.GetService<IDictionaryManager<LocalizationUkraineSettings>>()
            .GetRecordsAsync("1 = 1")).FirstOrDefault();

        var returnType = settings?.VatReturnType;
        if (string.IsNullOrWhiteSpace(returnType))
        {
            context.AddClientAction(ClientAction.Message(
                "Не вказано тип декларації в налаштуваннях України — вивантажувати нема за яким набором рядків."));
            return;
        }

        var id = await context.GetService<IUaTaxFiling>().ExportAsync(document.MetaId, returnType);
        if (id is null)
        {
            context.AddClientAction(ClientAction.Message("Декларацію не знайдено."));
            return;
        }

        context.AddClientAction(ClientAction.Message(
            "Вивантаження сформовано: Довідники → Вивантаження декларації."));
    }
}
