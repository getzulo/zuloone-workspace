#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ZuloOne.Runtime.Generated;

public partial class AbcProfileEventHandler : TypedDictionaryEventHandler<AbcProfile>
{
    private static readonly string[] Subjects = { "Item", "Customer" };
    private static readonly string[] Measures = { "Revenue", "InventoryValue", "StockQty" };
    private static readonly string[] AbcMethods = { "CumulativeShare", "Absolute", "Rank" };
    private static readonly string[] XyzMethods = { "Cv", "ZeroBuckets", "None" };

    public override async Task<EventResult> OnBeforeCreateAsync(AbcProfile record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        if (record.WindowMonths <= 0) record.WindowMonths = 12;
        if (record.BucketCount <= 0) record.BucketCount = 12;
        if (string.IsNullOrWhiteSpace(record.Subject)) record.Subject = "Item";
        if (string.IsNullOrWhiteSpace(record.Measure)) record.Measure = "Revenue";
        if (string.IsNullOrWhiteSpace(record.AbcMethod)) record.AbcMethod = "CumulativeShare";
        if (string.IsNullOrWhiteSpace(record.XyzMethod)) record.XyzMethod = "Cv";
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(AbcProfile record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Укажите наименование профиля");
        if (!Subjects.Contains(record.Subject))
            return EventResult.Cancel("Объект: Item или Customer");
        if (!Measures.Contains(record.Measure))
            return EventResult.Cancel("Показатель: Revenue, InventoryValue или StockQty");
        if (!AbcMethods.Contains(record.AbcMethod))
            return EventResult.Cancel("Метод ABC: CumulativeShare, Absolute или Rank");
        if (!XyzMethods.Contains(record.XyzMethod))
            return EventResult.Cancel("Метод XYZ: Cv, ZeroBuckets или None");
        if (record.WindowMonths <= 0)
            return EventResult.Cancel("Окно в месяцах должно быть больше нуля");
        if (record.BucketCount <= 0)
            return EventResult.Cancel("Число корзин XYZ должно быть больше нуля");
        return EventResult.Ok();
    }
}
