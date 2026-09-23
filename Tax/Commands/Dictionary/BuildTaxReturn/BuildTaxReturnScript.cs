#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// «Сформировать декларацию» — единственная дверь пользователя к
// ITaxReturnService.BuildFromPeriodAsync. Сервис и его девять тестов жили без
// неё: декларация собиралась только из тестов, а в интерфейсе оставалось
// завести TaxReturn руками и вбить OutputTax / InputTax / строки глазами по
// леджеру — при том что сервис считает их сам.
//
// Команда сидит на СПРАВОЧНИКЕ периода, а не на документе: BuildFromPeriodAsync
// декларацию СОЗДАЁТ, и кнопка на самой декларации плодила бы вторую.
// Ровно поэтому же её нет среди «не зови из команды» AfterPost-API: там
// перечислено то, что уже зовут события при Save, а эту не зовёт никто.
public partial class BuildTaxReturnCommand
{
    // Тип документа «Налоговая декларация»: ClientAction.OpenDocument принимает
    // metaId ТИПА, а не его имя.
    private static readonly Guid TaxReturnType = Guid.Parse("fdba6c82-e480-4aea-8ca3-1cb91e04c6df");

    public override async Task ExecuteAsync(TaxPeriod record, CommandContext context)
    {
        try
        {
            var id = await context.GetService<ITaxReturnService>().BuildFromPeriodAsync(record.MetaId);
            context.AddClientAction(ClientAction.Message(
                $"Декларация за период «{record.Code}» сформирована.", "success"));
            context.AddClientAction(ClientAction.OpenDocument(TaxReturnType, id));
        }
        catch (InvalidOperationException ex)
        {
            // Закрытый налоговый период, закрытый финансовый период, уже собранная
            // декларация — сервис отказывает текстом, пригодным для пользователя.
            context.AddClientAction(ClientAction.Message(ex.Message));
        }
    }
}
