#nullable enable
using System.Threading.Tasks;

namespace ZuloOne.Runtime.Generated;

public partial class WorkCenterEventHandler : TypedDictionaryEventHandler<WorkCenter>
{
    public override Task<EventResult> OnBeforeSaveAsync(
        WorkCenter record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
            return Task.FromResult(EventResult.Cancel("Укажите название рабочего центра"));
        record.Name = record.Name.Trim();
        return Task.FromResult(EventResult.Ok());
    }
}
