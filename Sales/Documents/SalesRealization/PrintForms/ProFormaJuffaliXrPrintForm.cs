#nullable enable
using System.Threading.Tasks;

// Static replica of the Juffali / Kleemann pro forma: shading, borders,
// logos and the QR live in the XR layout. The data script stays empty so
// preview paints the sample even when no invoice is selected.
public partial class ProFormaJuffaliXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(new { Note = "" });

    public override Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        table.Add(new { Note = "" });
        return Task.FromResult(table);
    }
}
