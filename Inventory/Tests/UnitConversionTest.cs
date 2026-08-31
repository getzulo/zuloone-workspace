using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Свойства НОВОЙ модели пересчёта, которых у попарных правил не было в принципе.
// Каждый кейс здесь отличает новую модель от старой, а не просто «пересчёт
// работает»: транзитивность без третьего правила, отказ между величинами,
// зависимость упаковки от товара.
public class UnitConversionTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private async Task<Guid> NewClassAsync(string name)
    {
        var c = DictionaryManager.NewRecord<UnitClass>();
        c.Code = $"{name}-{Db.NewId():N}"[..12];
        c.Name = name;
        return (await DictionaryManager.SaveRecordAsync(c)).MetaId;
    }

    private async Task<Guid> NewUnitAsync(string name, Guid unitClass, decimal? ratio, int decimals = 3)
    {
        var u = DictionaryManager.NewRecord<UnitOfMeasure>();
        u.Name = name;
        u.Code = $"{name[..1]}{Db.NewId():N}"[..8];
        u.DecimalPlaces = decimals;
        u.UnitClass = unitClass;
        if (ratio.HasValue) u.RatioToBase = ratio.Value;
        return (await DictionaryManager.SaveRecordAsync(u)).MetaId;
    }

    private async Task<Guid> NewItemAsync(Guid baseUnit)
    {
        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G-{Db.NewId():N}"[..12];
        group.Name = "Goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = baseUnit;
        return (await DictionaryManager.SaveRecordAsync(item)).MetaId;
    }

    private async Task<ItemUnit> NewPackAsync(Guid item, Guid unit, decimal qty)
    {
        var pack = DictionaryManager.NewRecord<ItemUnit>();
        pack.Item = item;
        pack.Unit = unit;
        pack.QtyInBaseUnit = qty;
        return await DictionaryManager.SaveRecordAsync(pack);
    }

    [IntegrationTest("Транзитивность: тонна в граммы считается без правила «тонна-грамм»")]
    public async Task TransitiveWithoutAPairRule()
    {
        var mass = await NewClassAsync("Mass");
        var gram = await NewUnitAsync("Gram", mass, 1m, 0);
        var kilo = await NewUnitAsync("Kilo", mass, 1000m, 3);
        var tonne = await NewUnitAsync("Tonne", mass, 1000000m, 3);

        var svc = GetService<IUnitConverter>();

        // Правил между парами не заводили ВООБЩЕ — только коэффициенты к грамму.
        Assert.IsTrue(await svc.ConvertAsync(1m, tonne, gram) == 1000000m,
            "1 т = 1 000 000 г, факт {0}", await svc.ConvertAsync(1m, tonne, gram));
        Assert.IsTrue(await svc.ConvertAsync(1m, tonne, kilo) == 1000m,
            "1 т = 1000 кг, факт {0}", await svc.ConvertAsync(1m, tonne, kilo));
        Assert.IsTrue(await svc.ConvertAsync(2500m, gram, kilo) == 2.5m,
            "2500 г = 2.5 кг, факт {0}", await svc.ConvertAsync(2500m, gram, kilo));
    }

    [IntegrationTest("Между разными величинами перевода нет — килограмм в метр невыразим")]
    public async Task CrossClassIsRefused()
    {
        var mass = await NewClassAsync("Mass");
        var length = await NewClassAsync("Length");
        var kilo = await NewUnitAsync("Kilo", mass, 1000m);
        var metre = await NewUnitAsync("Metre", length, 100m);

        var svc = GetService<IUnitConverter>();

        Assert.IsNull(await svc.ConvertAsync(1m, kilo, metre),
            "масса в длину не переводится ни при каких коэффициентах");
        Assert.IsNull(await svc.FactorAsync(kilo, metre), "и коэффициента между ними нет");
    }

    [IntegrationTest("Единица сама в себя возвращает количество, а не «правила нет»")]
    public async Task IdentityReturnsValue()
    {
        var mass = await NewClassAsync("Mass");
        var kilo = await NewUnitAsync("Kilo", mass, 1000m);

        // Если бы тождество давало null, платформа отклоняла бы КАЖДУЮ строку,
        // введённую в базовой единице товара, — то есть почти все.
        Assert.IsTrue(await GetService<IUnitConverter>().ConvertAsync(7m, kilo, kilo) == 7m,
            "перевод единицы в себя = исходное количество");
    }

    [IntegrationTest("Ящик одного товара — 12 штук, другого — 6: коэффициент принадлежит товару")]
    public async Task PackagingBelongsToTheItem()
    {
        var count = await NewClassAsync("Count");
        var piece = await NewUnitAsync("Piece", count, 1m, 0);
        // У ящика коэффициента НЕТ намеренно: без товара он ничего не значит.
        var box = await NewUnitAsync("Box", count, null, 0);

        var water = await NewItemAsync(piece);
        var juice = await NewItemAsync(piece);
        await NewPackAsync(water, box, 12m);
        await NewPackAsync(juice, box, 6m);

        var svc = GetService<IItemQuantityConverter>();

        var waterQty = await svc.ToBaseAsync(water, 1m, box);
        var juiceQty = await svc.ToBaseAsync(juice, 1m, box);

        Assert.IsTrue(waterQty == 12m, "ящик воды = 12 шт, факт {0}", waterQty);
        Assert.IsTrue(juiceQty == 6m, "ящик сока = 6 шт, факт {0}", juiceQty);
        Assert.IsTrue(waterQty != juiceQty,
            "одна и та же единица даёт РАЗНОЕ количество у разных товаров — ради этого всё и делалось");
    }

    [IntegrationTest("Товар без упаковки: перевода нет, а не молчаливая единица")]
    public async Task ItemWithoutPackagingHasNoRule()
    {
        var count = await NewClassAsync("Count");
        var piece = await NewUnitAsync("Piece", count, 1m, 0);
        var box = await NewUnitAsync("Box", count, null, 0);
        var item = await NewItemAsync(piece);

        Assert.IsNull(await GetService<IItemQuantityConverter>().ToBaseAsync(item, 1m, box),
            "упаковка не заведена — перевести нечем, и это должно быть видно");
    }

    [IntegrationTest("Упаковка проверяется: неположительный коэффициент и дубль отклоняются")]
    public async Task PackagingIsValidated()
    {
        var count = await NewClassAsync("Count");
        var piece = await NewUnitAsync("Piece", count, 1m, 0);
        var box = await NewUnitAsync("Box", count, null, 0);
        var item = await NewItemAsync(piece);

        var zeroRejected = false;
        try { await NewPackAsync(item, box, 0m); }
        catch { zeroRejected = true; }
        Assert.IsTrue(zeroRejected, "коэффициент 0 должен быть отклонён");

        var selfRejected = false;
        try { await NewPackAsync(item, piece, 1m); }
        catch { selfRejected = true; }
        Assert.IsTrue(selfRejected, "упаковка в базовой единице товара бессмысленна и должна отклоняться");
    }
}
