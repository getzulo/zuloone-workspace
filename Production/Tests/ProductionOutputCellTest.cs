using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Изделие и компоненты могут лежать в разных ячейках. Пустая ячейка выпуска
// на новом заказе копируется из настроек модуля; компоненты остаются на ячейке
// списания. Настройка — одиночная запись и переживает откат теста, поэтому
// прежнее значение возвращается в finally.
public class ProductionOutputCellTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private static Task<decimal> OnHandAsync(Guid cell, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item });

    [IntegrationTest("Изделие приходуется на ячейку выпуска, компонент списывается с ячейки заказа")]
    public async Task OutputCellReceivesTheProduct()
    {
        var consume = Db.NewId();
        var output = Db.NewId();
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-ячейка");
        var comp = await newItem("Компонент-ячейка");

        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = consume, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product.MetaId;
        order.Quantity = 2m;
        order.Location = consume;
        order.OutputLocation = output;
        order.Components.Add(new ProductionOrderComponentsTablePartRow { Component = comp.MetaId, QtyRequired = 4m });
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        var compLeft = await OnHandAsync(consume, comp.MetaId);
        var productOnConsume = await OnHandAsync(consume, product.MetaId);
        var productOnOutput = await OnHandAsync(output, product.MetaId);
        var compOnOutput = await OnHandAsync(output, comp.MetaId);
        Assert.IsTrue(compLeft == 6m, "компонент 10 − 4 = 6 на ячейке списания, факт {0}", compLeft);
        Assert.IsTrue(productOnConsume == 0m, "на ячейке списания изделия нет, факт {0}", productOnConsume);
        Assert.IsTrue(productOnOutput == 2m, "изделие +2 на ячейке выпуска, факт {0}", productOnOutput);
        Assert.IsTrue(compOnOutput == 0m, "на ячейке выпуска компонента нет, факт {0}", compOnOutput);
    }

    [IntegrationTest("Пустая ячейка выпуска нового заказа берётся из настроек модуля")]
    public async Task DefaultOutputLocationFillsANewOrder()
    {
        var rows = await DictionaryManager.GetRecordsAsync<ProductionSettings>();
        var settings = rows.FirstOrDefault();
        var created = settings == null;
        if (settings == null)
        {
            settings = DictionaryManager.NewRecord<ProductionSettings>();
            settings.AutoExpandBom = false;
        }
        var prior = settings.DefaultOutputLocation;
        var output = Db.NewId();
        try
        {
            settings.DefaultOutputLocation = output;
            settings = await DictionaryManager.SaveRecordAsync(settings);

            var consume = Db.NewId();
            var newItem = await ItemFactoryAsync();
            var product = await newItem("Изделие-умолчание");
            var comp = await newItem("Компонент-умолчание");
            await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
                new Dictionary<string, object?> { ["Cell"] = consume, ["Item"] = comp.MetaId },
                new Dictionary<string, decimal> { ["Qty"] = 10m });

            var preview = await GetService<IRecordDefaults>().ForNewAsync("ProductionOrder");
            Assert.IsTrue(preview.TryGetValue("OutputLocation", out var previewCell)
                && previewCell is Guid previewId && previewId == output,
                "карточка до сохранения уже с ячейкой из настроек");

            var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
            Assert.IsTrue(order.OutputLocation == output,
                "новый заказ открывается с ячейкой из настроек, факт {0}", order.OutputLocation);
            order.Product = product.MetaId;
            order.Quantity = 2m;
            order.Location = consume;
            order.Components.Add(new ProductionOrderComponentsTablePartRow { Component = comp.MetaId, QtyRequired = 4m });
            await DocumentManager.SaveDocumentAsync(order);

            var stored = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
            Assert.IsTrue(stored!.OutputLocation == output,
                "новый заказ без своей ячейки берёт умолчание настроек");

            stored.Subtype = ProductionOrder.Subtypes.Finished;
            await DocumentManager.SaveDocumentAsync(stored);

            var productOnOutput = await OnHandAsync(output, product.MetaId);
            var productOnConsume = await OnHandAsync(consume, product.MetaId);
            var compLeft = await OnHandAsync(consume, comp.MetaId);
            Assert.IsTrue(productOnOutput == 2m, "изделие +2 на ячейке из настроек, факт {0}", productOnOutput);
            Assert.IsTrue(productOnConsume == 0m, "на ячейке списания изделия нет, факт {0}", productOnConsume);
            Assert.IsTrue(compLeft == 6m, "компонент 10 − 4 = 6, факт {0}", compLeft);
        }
        finally
        {
            settings.DefaultOutputLocation = created ? Guid.Empty : prior;
            await DictionaryManager.SaveRecordAsync(settings);
        }
    }

    private async Task<Func<string, Task<Item>>> ItemFactoryAsync()
    {
        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = "PCS";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "PROD";
        group.Name = "Производство";
        group = await DictionaryManager.SaveRecordAsync(group);

        return async name =>
        {
            var item = DictionaryManager.NewRecord<Item>();
            item.Name = name;
            item.ItemGroup = group.MetaId;
            item.UnitOfMeasure = uom.MetaId;
            return await DictionaryManager.SaveRecordAsync(item);
        };
    }
}
